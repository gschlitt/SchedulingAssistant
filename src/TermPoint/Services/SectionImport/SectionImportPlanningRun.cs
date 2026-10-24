using TermPoint.Models;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// One execution of the section-import planner's first part (step 24.3): it groups the rows of a
/// file into sections and resolves every value they name to an existing record, a new record, or a
/// problem. Created by <see cref="SectionImportPlanner.Plan"/> for a single call and then discarded;
/// it holds the state of that call (the registries of new records, the problems found so far) and
/// touches nothing outside its two inputs.
/// <para>
/// Order of work: whole-file semester check; per-row required-cell and semester checks; grouping;
/// then, section by section in order of first appearance, subject, course, the already-exists check
/// and the section's and meetings' values. Every problem found is collected, not just the first.
/// </para>
/// </summary>
internal sealed class SectionImportPlanningRun
{
    // ── Inputs and results ────────────────────────────────────────────────────

    private readonly ImportSheet _sheet;
    private readonly ImportSetupSnapshot _snapshot;
    private readonly List<ImportProblem> _problems = new();
    private readonly List<SectionDraft> _newSections = new();
    private readonly List<SectionDraft> _skippedSections = new();

    // ── Registries: existing records, plus the new ones this run plans ───────

    private readonly SetupRegistry<Subject, SetupRef<Subject>> _subjects;
    private readonly SetupRegistry<Course, CourseRef> _courses;
    private readonly SetupRegistry<Instructor, InstructorRef> _instructors;
    private readonly SetupRegistry<SchedulingEnvironmentValue, SetupRef<SchedulingEnvironmentValue>> _sectionTypes;
    private readonly SetupRegistry<SchedulingEnvironmentValue, SetupRef<SchedulingEnvironmentValue>> _meetingTypes;
    private readonly SetupRegistry<SchedulingEnvironmentValue, SetupRef<SchedulingEnvironmentValue>> _tags;
    private readonly SetupRegistry<Campus, SetupRef<Campus>> _campuses;
    private readonly SetupRegistry<Room, SetupRef<Room>> _rooms;

    // ── Lookups the registries do not cover ───────────────────────────────────

    /// <summary>The existing rooms by room number alone, for rows that leave Building blank.</summary>
    private readonly Dictionary<string, List<Room>> _existingRoomsByNumber;

    /// <summary>The section codes already in the open semester, per course id.</summary>
    private readonly Dictionary<string, HashSet<string>> _existingSectionCodes;

    /// <summary>Initials in use: every existing instructor's plus those generated earlier in this run.</summary>
    private readonly HashSet<string> _takenInitials;

    /// <summary>The first non-blank Course Title of each course code, over all usable rows.</summary>
    private readonly Dictionary<string, string> _courseTitles = new(NameMatch.CodeComparer);

    /// <summary>Subject resolutions by typed value; null records a failure, which is reported once.</summary>
    private readonly Dictionary<string, SetupRef<Subject>?> _subjectResolutions = new(NameMatch.Comparer);

    /// <summary>Course resolutions by code; null records a failure, which is reported once.</summary>
    private readonly Dictionary<string, CourseRef?> _courseResolutions = new(NameMatch.CodeComparer);

    /// <summary>
    /// Creates a run over one file and one snapshot, indexing the snapshot.
    /// </summary>
    /// <param name="sheet">The rows of the import file.</param>
    /// <param name="snapshot">The database state to match against.</param>
    public SectionImportPlanningRun(ImportSheet sheet, ImportSetupSnapshot snapshot)
    {
        _sheet = sheet;
        _snapshot = snapshot;

        _subjects = new(snapshot.Subjects, s => s.CalendarAbbreviation, NameMatch.Comparer,
            SetupRef<Subject>.Existing);
        _courses = new(snapshot.Courses, c => c.CalendarCode, NameMatch.CodeComparer,
            c => new CourseRef(c, isNew: false, firstRow: null, subject: null));
        _instructors = new(snapshot.Instructors, i => PersonKey(i.LastName, i.FirstName), NameMatch.Comparer,
            i => new InstructorRef(i, isNew: false, firstRow: null));
        _sectionTypes = NewNamedRegistry(snapshot.SectionTypes);
        _meetingTypes = NewNamedRegistry(snapshot.MeetingTypes);
        _tags = NewNamedRegistry(snapshot.Tags);
        _campuses = new(snapshot.Campuses, c => c.Name, NameMatch.Comparer, SetupRef<Campus>.Existing,
            c => c.SortOrder);
        _rooms = new(snapshot.Rooms, r => PersonKey(r.Building, r.RoomNumber), NameMatch.Comparer,
            SetupRef<Room>.Existing, r => r.SortOrder);

        _existingRoomsByNumber = new Dictionary<string, List<Room>>(NameMatch.Comparer);
        foreach (var room in snapshot.Rooms)
        {
            if (!_existingRoomsByNumber.TryGetValue(room.RoomNumber, out var list))
                _existingRoomsByNumber[room.RoomNumber] = list = new List<Room>();
            list.Add(room);
        }

        _existingSectionCodes = new Dictionary<string, HashSet<string>>();
        foreach (var section in snapshot.Sections)
        {
            if (section.CourseId is null)
                continue;
            if (!_existingSectionCodes.TryGetValue(section.CourseId, out var codes))
                _existingSectionCodes[section.CourseId] = codes = new HashSet<string>(NameMatch.Comparer);
            codes.Add(section.SectionCode);
        }

        _takenInitials = new HashSet<string>(
            snapshot.Instructors.Where(i => !string.IsNullOrWhiteSpace(i.Initials)).Select(i => i.Initials.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Builds a registry for setup values matched by <c>Name</c> (section types, meeting types, tags).</summary>
    /// <param name="existing">The existing values of one type.</param>
    /// <returns>A registry whose new values continue the type's sort order.</returns>
    private static SetupRegistry<SchedulingEnvironmentValue, SetupRef<SchedulingEnvironmentValue>> NewNamedRegistry(
        IEnumerable<SchedulingEnvironmentValue> existing) =>
        new(existing, v => v.Name, NameMatch.Comparer, SetupRef<SchedulingEnvironmentValue>.Existing, v => v.SortOrder);

    // ── The run ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the planner and returns its report. Warnings are left empty (step 24.4 fills them).
    /// </summary>
    /// <returns>The report: problems, skipped sections and the plan.</returns>
    public ImportReport Execute()
    {
        var semesterWasEmpty = _snapshot.Sections.Count == 0;

        // The whole file naming another semester is one problem, and nothing else is checked.
        var wholeFileProblem = FindWholeFileSemesterProblem();
        if (wholeFileProblem is not null)
            return new ImportReport(new[] { wholeFileProblem }, Array.Empty<ImportWarning>(),
                Array.Empty<SectionDraft>(), ImportPlan.Empty, semesterWasEmpty);

        var usableRows = SelectUsableRows();
        CollectCourseTitles(usableRows);

        foreach (var group in GroupRows(usableRows))
            ResolveGroup(group);

        AssignCampusesToNewRooms();

        var plan = new ImportPlan(
            _subjects.NewRefs, _campuses.NewRefs, _sectionTypes.NewRefs, _meetingTypes.NewRefs,
            _tags.NewRefs, _rooms.NewRefs, _courses.NewRefs, _instructors.NewRefs, _newSections);

        // Stable sort: file-wide problems first, then by row, keeping the order found within a row.
        var problems = _problems.OrderBy(p => p.Row ?? 0).ToList();
        return new ImportReport(problems, Array.Empty<ImportWarning>(), _skippedSections, plan, semesterWasEmpty);
    }

    // ── Rows: required cells and the semester ─────────────────────────────────

    /// <summary>
    /// Finds the single file-wide problem for a file whose every row names the same semester, when
    /// that semester is not the open one. Rows without both an Academic Year and a Term are not
    /// counted (they get their own "is empty" problems when the file is not rejected here).
    /// </summary>
    /// <returns>The problem, or null when the file does not name one other semester throughout.</returns>
    private ImportProblem? FindWholeFileSemesterProblem()
    {
        var pairs = _sheet.Rows
            .Select(r => (AcademicYear: r.Cell(SectionImportColumns.AcademicYear), Term: r.Cell(SectionImportColumns.Term)))
            .Where(p => p.AcademicYear.Length > 0 && p.Term.Length > 0)
            .ToList();
        if (pairs.Count == 0)
            return null;

        var first = pairs[0];
        var allAgree = pairs.All(p => NamesTheSameSemester(p.AcademicYear, p.Term, first.AcademicYear, first.Term));
        if (!allAgree || NamesOpenSemester(first.AcademicYear, first.Term))
            return null;

        return new ImportProblem(null, null,
            $"This file is for {first.Term} ({first.AcademicYear}), but {OpenSemesterText()} is open. " +
            $"Open {first.Term} to import it, or correct the {SectionImportColumns.AcademicYear} and {SectionImportColumns.Term} columns.");
    }

    /// <summary>
    /// Checks the required key cells and the semester of every row, reporting each fault, and returns
    /// the rows that passed both checks (in file order). A row with any fault is left out of grouping.
    /// </summary>
    /// <returns>The usable rows.</returns>
    private List<ImportRow> SelectUsableRows()
    {
        var required = SectionImportColumns.All.Where(c => c.AlwaysRequired).Select(c => c.Name).ToList();
        var usable = new List<ImportRow>();

        foreach (var row in _sheet.Rows)
        {
            var usableRow = true;

            foreach (var column in required.Where(c => row.Cell(c).Length == 0))
            {
                AddProblem(row.RowNumber, column, $"{column} is empty.");
                usableRow = false;
            }

            var academicYear = row.Cell(SectionImportColumns.AcademicYear);
            var term = row.Cell(SectionImportColumns.Term);
            if (academicYear.Length > 0 && term.Length > 0 && !NamesOpenSemester(academicYear, term))
            {
                AddProblem(row.RowNumber, null,
                    $"This row is for {term} ({academicYear}), but {OpenSemesterText()} is open.");
                usableRow = false;
            }

            if (usableRow)
                usable.Add(row);
        }
        return usable;
    }

    /// <summary>True when the Academic Year and Term name the open semester (ignoring case and spacing).</summary>
    /// <param name="academicYear">The Academic Year cell.</param>
    /// <param name="term">The Term cell.</param>
    private bool NamesOpenSemester(string academicYear, string term) =>
        NamesTheSameSemester(academicYear, term, _snapshot.AcademicYear.Name, _snapshot.Semester.Name);

    /// <summary>True when two (Academic Year, Term) pairs are equal, ignoring case and spacing.</summary>
    private static bool NamesTheSameSemester(string academicYearA, string termA, string academicYearB, string termB) =>
        NameMatch.Equal(academicYearA, academicYearB) && NameMatch.Equal(termA, termB);

    /// <summary>The open semester as the messages name it, e.g. <c>Fall (2026-2027)</c>.</summary>
    private string OpenSemesterText() => $"{_snapshot.Semester.Name} ({_snapshot.AcademicYear.Name})";

    // ── Grouping ──────────────────────────────────────────────────────────────

    /// <summary>The rows of one section, with the key as spelled on its first row.</summary>
    private sealed class RowGroup
    {
        /// <summary>The section's key.</summary>
        public required SectionKey Key { get; init; }

        /// <summary>The section's rows in file order.</summary>
        public List<ImportRow> Rows { get; } = new();
    }

    /// <summary>
    /// Groups rows by <see cref="SectionKey"/> in order of first appearance; the rows of a group keep
    /// file order, even when they are not adjacent in the file.
    /// </summary>
    /// <param name="rows">The usable rows in file order.</param>
    /// <returns>The groups in order of first appearance.</returns>
    private static List<RowGroup> GroupRows(IEnumerable<ImportRow> rows)
    {
        var groups = new List<RowGroup>();
        var byKey = new Dictionary<SectionKey, RowGroup>();

        foreach (var row in rows)
        {
            var key = new SectionKey(
                row.Cell(SectionImportColumns.Subject),
                row.Cell(SectionImportColumns.CourseNumber),
                row.Cell(SectionImportColumns.SectionCode));
            if (!byKey.TryGetValue(key, out var group))
            {
                group = new RowGroup { Key = key };
                byKey[key] = group;
                groups.Add(group);
            }
            group.Rows.Add(row);
        }
        return groups;
    }

    /// <summary>
    /// Records, for each course code in the file, its first non-blank Course Title over all rows (in
    /// file order). A new course takes this title, however far down the file it is written.
    /// </summary>
    /// <param name="rows">The usable rows in file order.</param>
    private void CollectCourseTitles(IEnumerable<ImportRow> rows)
    {
        foreach (var row in rows)
        {
            var title = row.Cell(SectionImportColumns.CourseTitle);
            if (title.Length == 0)
                continue;
            var code = row.Cell(SectionImportColumns.Subject) + row.Cell(SectionImportColumns.CourseNumber);
            _courseTitles.TryAdd(code, title);
        }
    }

    // ── One section ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves one section: its subject and course, whether it already exists (then it is skipped and
    /// nothing else on its rows is looked at), and otherwise its section-level and meeting values.
    /// A section whose subject or course cannot be resolved is not examined further: the fault is
    /// reported once, and the section's other cells may be irrelevant once it is fixed.
    /// </summary>
    /// <param name="group">The section's rows.</param>
    private void ResolveGroup(RowGroup group)
    {
        var firstRow = group.Rows[0];

        var subject = ResolveSubject(group.Key.Subject, firstRow.RowNumber);
        if (subject is null)
            return;
        var course = ResolveCourse(subject, group.Key.CourseNumber, firstRow.RowNumber);
        if (course is null)
            return;

        var rowNumbers = group.Rows.Select(r => r.RowNumber).ToList();

        if (!course.IsNew && SectionExists(course.Record.Id, group.Key.SectionCode))
        {
            _skippedSections.Add(new SectionDraft
            {
                Key = group.Key,
                RowNumbers = rowNumbers,
                Rows = group.Rows,
                Course = course,
                AlreadyExists = true,
            });
            return;
        }

        _newSections.Add(new SectionDraft
        {
            Key = group.Key,
            RowNumbers = rowNumbers,
            Rows = group.Rows,
            Course = course,
            Instructors = ResolveInstructors(group.Rows),
            SectionType = ResolveFirstNamed(group.Rows, SectionImportColumns.SectionType, _sectionTypes,
                "section types", NewEnvironmentValue),
            Campus = ResolveFirstNamed(group.Rows, SectionImportColumns.Campus, _campuses,
                "campuses", NewCampus),
            Tags = ResolveTags(group.Rows),
            RawCapacity = ImportRowText.FirstNonBlank(group.Rows, SectionImportColumns.Capacity)?.Text,
            MeetingRows = group.Rows.Select(ResolveMeetingRow).ToList(),
        });
    }

    /// <summary>True when the open semester already has a section of this course with this code.</summary>
    /// <param name="courseId">The existing course's id.</param>
    /// <param name="sectionCode">The section code as typed.</param>
    private bool SectionExists(string courseId, string sectionCode) =>
        _existingSectionCodes.TryGetValue(courseId, out var codes) && codes.Contains(sectionCode);

    // ── Subject and course ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the Subject cell by calendar abbreviation: one match is used, none plans a new subject
    /// (its name is the abbreviation too, unless that is another subject's name), two or more is a
    /// problem. Each distinct value is resolved, and any problem reported, once.
    /// </summary>
    /// <param name="typed">The Subject cell as typed (trimmed).</param>
    /// <param name="row">The row that first names it.</param>
    /// <returns>The reference, or null when the value could not be resolved.</returns>
    private SetupRef<Subject>? ResolveSubject(string typed, int row)
    {
        if (_subjectResolutions.TryGetValue(typed, out var known))
            return known;

        var resolved = ResolveSubjectCore(typed, row);
        _subjectResolutions[typed] = resolved;
        return resolved;
    }

    /// <summary>Does the work of <see cref="ResolveSubject"/> for a value met for the first time.</summary>
    /// <param name="typed">The Subject cell as typed (trimmed).</param>
    /// <param name="row">The row that first names it.</param>
    /// <returns>The reference, or null after reporting a problem.</returns>
    private SetupRef<Subject>? ResolveSubjectCore(string typed, int row)
    {
        var matches = _subjects.ExistingMatches(typed);
        if (matches.Count > 1)
        {
            AddProblem(row, SectionImportColumns.Subject,
                $"There are {CountWord(matches.Count)} subjects with the abbreviation {typed} in TermPoint.");
            return null;
        }
        if (matches.Count == 1)
            return _subjects.ExistingRef(matches[0]);

        // A new subject's name is the abbreviation too, so it must not already be another subject's name.
        var sameName = _snapshot.Subjects.FirstOrDefault(s => NameMatch.Equal(s.Name, typed));
        if (sameName is not null)
        {
            AddProblem(row, SectionImportColumns.Subject,
                $"{typed} is the name of subject {sameName.CalendarAbbreviation}. " +
                $"Use {sameName.CalendarAbbreviation} in the {SectionImportColumns.Subject} column.");
            return null;
        }

        return _subjects.AddNew(typed, _ => SetupRef<Subject>.New(
            new Subject { Name = typed, CalendarAbbreviation = typed }, row));
    }

    /// <summary>
    /// Resolves the course named by a subject and a Course Number: the code is the subject's
    /// abbreviation plus the number, compared ignoring all spaces. One match is used, none plans a new
    /// course, two or more is a problem. Each distinct code is resolved, and any problem reported, once.
    /// </summary>
    /// <param name="subject">The course's subject (existing or new).</param>
    /// <param name="courseNumber">The Course Number cell as typed (trimmed).</param>
    /// <param name="row">The row that first names the course.</param>
    /// <returns>The reference, or null when the course could not be resolved.</returns>
    private CourseRef? ResolveCourse(SetupRef<Subject> subject, string courseNumber, int row)
    {
        var typedCode = subject.Record.CalendarAbbreviation + courseNumber;
        if (_courseResolutions.TryGetValue(typedCode, out var known))
            return known;

        var match = _courses.Resolve(typedCode, _ => BuildNewCourse(subject, typedCode, row));
        if (match.Ref is null)
        {
            var stored = match.Ambiguous.Select(c => c.CalendarCode).ToList();
            AddProblem(row, SectionImportColumns.CourseNumber,
                $"There are {CountWord(stored.Count)} courses in TermPoint with the code {typedCode}: {JoinWithAnd(stored)}.");
        }

        _courseResolutions[typedCode] = match.Ref;
        return match.Ref;
    }

    /// <summary>
    /// Builds the reference to a new course. Its code has no spaces (the app's convention), its title is
    /// the first non-blank Course Title of any of its rows, and it starts active with no level, tags
    /// or capacity. A course with no title anywhere in the file is a problem on its first row.
    /// </summary>
    /// <param name="subject">The course's subject.</param>
    /// <param name="typedCode">The subject abbreviation plus the Course Number, as typed.</param>
    /// <param name="row">The course's first row.</param>
    /// <returns>The new course reference.</returns>
    private CourseRef BuildNewCourse(SetupRef<Subject> subject, string typedCode, int row)
    {
        var code = NameMatch.NormalizeCode(typedCode);
        if (!_courseTitles.TryGetValue(typedCode, out var title))
        {
            title = string.Empty;
            AddProblem(row, SectionImportColumns.CourseTitle, $"{code} is a new course, so it needs a Course Title.");
        }

        var course = new Course
        {
            SubjectId = subject.Record.Id,
            CalendarCode = code,
            Title = title,
            IsActive = true,
            Level = string.Empty,
            Capacity = null,
        };
        return new CourseRef(course, isNew: true, firstRow: row, subject: subject);
    }

    // ── Section-level values ──────────────────────────────────────────────────

    /// <summary>
    /// Resolves a section-level column that holds one name (Section Type, Campus): the value comes
    /// from the first row where it is filled; blank gives null.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The section's rows in file order.</param>
    /// <param name="column">The template column.</param>
    /// <param name="registry">The registry of that kind of record.</param>
    /// <param name="pluralLabel">The kind's plural, for messages ("campuses").</param>
    /// <param name="build">Builds a new record from the typed name and its sort order.</param>
    /// <returns>The reference, or null when blank or ambiguous.</returns>
    private SetupRef<T>? ResolveFirstNamed<T>(
        IReadOnlyList<ImportRow> rows,
        string column,
        SetupRegistry<T, SetupRef<T>> registry,
        string pluralLabel,
        Func<string, int, T> build) where T : class
    {
        if (ImportRowText.FirstNonBlank(rows, column) is not { } hit)
            return null;
        return ResolveName(registry, hit.Text, hit.Row.RowNumber, column, pluralLabel, build);
    }

    /// <summary>
    /// Resolves a typed name against a registry, planning a new record when nothing matches. A name
    /// that matches two or more existing records is a fault in TermPoint's own data, reported once.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="registry">The registry of that kind of record.</param>
    /// <param name="typed">The name as typed (trimmed).</param>
    /// <param name="row">The row that supplies the name (where a problem is reported).</param>
    /// <param name="column">The template column the name is in.</param>
    /// <param name="pluralLabel">The kind's plural, for messages ("tags").</param>
    /// <param name="build">Builds a new record from the typed name and its sort order.</param>
    /// <returns>The reference, or null when ambiguous.</returns>
    private SetupRef<T>? ResolveName<T>(
        SetupRegistry<T, SetupRef<T>> registry,
        string typed,
        int row,
        string column,
        string pluralLabel,
        Func<string, int, T> build) where T : class
    {
        var match = registry.Resolve(typed, sortOrder => SetupRef<T>.New(build(typed, sortOrder), row));
        if (match.Ref is null && registry.MarkAmbiguityReported(typed))
            AddProblem(row, column,
                $"There are {CountWord(match.Ambiguous.Count)} {pluralLabel} called {typed} in TermPoint.");
        return match.Ref;
    }

    /// <summary>Builds a new section type, meeting type or tag, named as first typed.</summary>
    /// <param name="name">The typed name.</param>
    /// <param name="sortOrder">The sort order to give it.</param>
    private static SchedulingEnvironmentValue NewEnvironmentValue(string name, int sortOrder) =>
        new() { Name = name, SortOrder = sortOrder };

    /// <summary>Builds a new campus, named as first typed.</summary>
    /// <param name="name">The typed name.</param>
    /// <param name="sortOrder">The sort order to give it.</param>
    private static Campus NewCampus(string name, int sortOrder) => new() { Name = name, SortOrder = sortOrder };

    /// <summary>
    /// Resolves the Tags cell (taken from the first row where it is filled): names separated by
    /// semicolons, each matched or planned as new. A tag listed twice in the cell is a problem.
    /// </summary>
    /// <param name="rows">The section's rows in file order.</param>
    /// <returns>The tags in the order listed, each once.</returns>
    private List<SetupRef<SchedulingEnvironmentValue>> ResolveTags(IReadOnlyList<ImportRow> rows)
    {
        var tags = new List<SetupRef<SchedulingEnvironmentValue>>();
        if (ImportRowText.FirstNonBlank(rows, SectionImportColumns.Tags) is not { } hit)
            return tags;

        // The first spelling of each tag, so a repeat is reported by the name as first typed.
        var firstTyped = new Dictionary<string, string>(NameMatch.Comparer);
        var reportedTwice = new HashSet<string>(NameMatch.Comparer);
        foreach (var name in SplitList(hit.Text))
        {
            if (!firstTyped.TryAdd(name, name))
            {
                if (reportedTwice.Add(name))
                    AddProblem(hit.Row.RowNumber, SectionImportColumns.Tags,
                        $"{firstTyped[name]} is listed twice in {SectionImportColumns.Tags}.");
                continue;
            }

            var tag = ResolveName(_tags, name, hit.Row.RowNumber, SectionImportColumns.Tags, "tags", NewEnvironmentValue);
            if (tag is not null)
                tags.Add(tag);
        }
        return tags;
    }

    // ── Instructors ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the Instructor(s) cell (taken from the first row where it is filled): entries
    /// separated by semicolons, each <c>Last, First</c> split at the first comma. An existing
    /// instructor matches on both names; otherwise one new instructor is planned per distinct name in
    /// the whole file. A missing last name, a person listed twice, or two existing instructors with the
    /// same name is a problem.
    /// </summary>
    /// <param name="rows">The section's rows in file order.</param>
    /// <returns>The instructors in the order listed.</returns>
    private List<InstructorRef> ResolveInstructors(IReadOnlyList<ImportRow> rows)
    {
        var instructors = new List<InstructorRef>();
        if (ImportRowText.FirstNonBlank(rows, SectionImportColumns.Instructors) is not { } hit)
            return instructors;

        var row = hit.Row.RowNumber;
        // The first spelling of each person, so a repeat is reported by the name as first typed.
        var firstTyped = new Dictionary<string, string>(NameMatch.Comparer);
        var reportedTwice = new HashSet<string>(NameMatch.Comparer);

        foreach (var entry in SplitList(hit.Text))
        {
            var (last, first) = ParseInstructor(entry);
            if (last.Length == 0)
            {
                AddProblem(row, SectionImportColumns.Instructors,
                    "Each instructor needs a last name, written Last, First.");
                continue;
            }

            var key = PersonKey(last, first);
            var displayName = first.Length > 0 ? $"{last}, {first}" : last;
            if (!firstTyped.TryAdd(key, displayName))
            {
                if (reportedTwice.Add(key))
                    AddProblem(row, SectionImportColumns.Instructors,
                        $"{firstTyped[key]} is listed twice in {SectionImportColumns.Instructors}.");
                continue;
            }

            var match = _instructors.Resolve(key, _ => BuildNewInstructor(last, first, row));
            if (match.Ref is not null)
                instructors.Add(match.Ref);
            else if (_instructors.MarkAmbiguityReported(key))
                AddProblem(row, SectionImportColumns.Instructors,
                    $"There are {CountWord(match.Ambiguous.Count)} instructors called {displayName} in TermPoint.");
        }
        return instructors;
    }

    /// <summary>
    /// Builds the reference to a new instructor: active, with initials that are free in the database
    /// and in this import so far (the result is added to the taken set so the next one differs).
    /// </summary>
    /// <param name="last">The last name as typed.</param>
    /// <param name="first">The first name as typed; may be empty.</param>
    /// <param name="row">The row that names the instructor first.</param>
    /// <returns>The new instructor reference.</returns>
    private InstructorRef BuildNewInstructor(string last, string first, int row)
    {
        var initials = InstructorInitials.Generate(first, last, _takenInitials);
        _takenInitials.Add(initials);

        var instructor = new Instructor
        {
            LastName = last,
            FirstName = first,
            Initials = initials,
            IsActive = true,
        };
        return new InstructorRef(instructor, isNew: true, firstRow: row);
    }

    /// <summary>
    /// Splits one instructor entry at its <b>first</b> comma into last and first name, both trimmed.
    /// An entry with no comma is a last name only.
    /// </summary>
    /// <param name="entry">One entry of the Instructor(s) cell.</param>
    /// <returns>The last name and the first name (either may be empty).</returns>
    private static (string Last, string First) ParseInstructor(string entry)
    {
        var comma = entry.IndexOf(',');
        return comma < 0
            ? (entry.Trim(), string.Empty)
            : (entry[..comma].Trim(), entry[(comma + 1)..].Trim());
    }

    // ── Meeting rows ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves one row of a new section: its meeting type and its room, and keeps the raw days,
    /// times and frequency text for step 24.4.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The meeting row draft.</returns>
    private MeetingRowDraft ResolveMeetingRow(ImportRow row)
    {
        var meetingTypeText = row.Cell(SectionImportColumns.MeetingType);
        var meetingType = meetingTypeText.Length == 0
            ? null
            : ResolveName(_meetingTypes, meetingTypeText, row.RowNumber, SectionImportColumns.MeetingType,
                "meeting types", NewEnvironmentValue);

        return new MeetingRowDraft(
            row.RowNumber,
            row.Cell(SectionImportColumns.MeetingDays),
            row.Cell(SectionImportColumns.Begin),
            row.Cell(SectionImportColumns.End),
            row.Cell(SectionImportColumns.Frequency),
            meetingType,
            ResolveRoom(row));
    }

    /// <summary>
    /// Resolves a row's Building and Room. Both blank is no room. A Building alone is a problem. A Room
    /// alone must match exactly one existing room by number. Both filled match an existing room or plan
    /// a new one.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The room reference, or null when there is no room or it could not be resolved.</returns>
    private SetupRef<Room>? ResolveRoom(ImportRow row)
    {
        var building = row.Cell(SectionImportColumns.Building);
        var number = row.Cell(SectionImportColumns.Room);

        if (building.Length == 0 && number.Length == 0)
            return null;

        if (number.Length == 0)
        {
            AddProblem(row.RowNumber, SectionImportColumns.Room, $"Building {building} has no Room.");
            return null;
        }

        return building.Length == 0
            ? ResolveRoomByNumber(number, row.RowNumber)
            : ResolveRoomByBuildingAndNumber(building, number, row.RowNumber);
    }

    /// <summary>
    /// Resolves a room given by number alone. Only rooms already in TermPoint can match; the number
    /// must belong to exactly one of them.
    /// </summary>
    /// <param name="number">The Room cell as typed.</param>
    /// <param name="row">The row.</param>
    /// <returns>The existing room's reference, or null after reporting a problem.</returns>
    private SetupRef<Room>? ResolveRoomByNumber(string number, int row)
    {
        var matches = _existingRoomsByNumber.TryGetValue(number, out var list) ? list : new List<Room>();
        switch (matches.Count)
        {
            case 1:
                return _rooms.ExistingRef(matches[0]);

            case 0:
                AddProblem(row, SectionImportColumns.Building,
                    $"Room {number} isn't in TermPoint yet. To add it, give its building too.");
                return null;

            default:
                var buildings = matches.Select(r => r.Building).Distinct(NameMatch.Comparer).ToList();
                if (buildings.Count > 1)
                    AddProblem(row, SectionImportColumns.Building,
                        $"Room {number} is in more than one building ({string.Join(", ", buildings)}). " +
                        $"Fill in the {SectionImportColumns.Building}.");
                else
                    // Several rooms in one building share the number: a duplicate in TermPoint's own data.
                    AddProblem(row, SectionImportColumns.Room,
                        $"There are {CountWord(matches.Count)} rooms called {matches[0].Building} {number} in TermPoint.");
                return null;
        }
    }

    /// <summary>
    /// Resolves a room given by building and number: one existing room is used, none plans a new room
    /// (building and number as first typed, next sort order, no capacity or type), two or more is a
    /// problem in TermPoint's own data, reported once.
    /// </summary>
    /// <param name="building">The Building cell as typed.</param>
    /// <param name="number">The Room cell as typed.</param>
    /// <param name="row">The row.</param>
    /// <returns>The room reference, or null when ambiguous.</returns>
    private SetupRef<Room>? ResolveRoomByBuildingAndNumber(string building, string number, int row)
    {
        var key = PersonKey(building, number);
        var match = _rooms.Resolve(key, sortOrder => SetupRef<Room>.New(
            new Room { Building = building, RoomNumber = number, SortOrder = sortOrder }, row));

        if (match.Ref is null && _rooms.MarkAmbiguityReported(key))
            AddProblem(row, SectionImportColumns.Room,
                $"There are {CountWord(match.Ambiguous.Count)} rooms called {building} {number} in TermPoint.");
        return match.Ref;
    }

    /// <summary>
    /// Sets each new room's campus: the campus of every new section that meets in it, when they all
    /// name the same one (new or existing). If they differ, or any of them has no campus, the room's
    /// campus stays empty (an unassigned room is offered on every campus).
    /// </summary>
    private void AssignCampusesToNewRooms()
    {
        var campusIdsByRoom = new Dictionary<SetupRef<Room>, HashSet<string?>>(ReferenceEqualityComparer.Instance);

        foreach (var section in _newSections)
        {
            var campusId = section.Campus?.Record.Id;
            var newRooms = section.MeetingRows.Select(m => m.Room).Where(r => r is { IsNew: true }).Distinct();
            foreach (var room in newRooms)
            {
                if (!campusIdsByRoom.TryGetValue(room!, out var ids))
                    campusIdsByRoom[room!] = ids = new HashSet<string?>();
                ids.Add(campusId);
            }
        }

        foreach (var room in _rooms.NewRefs)
        {
            if (campusIdsByRoom.TryGetValue(room, out var ids) && ids.Count == 1 && ids.Single() is { } id)
                room.Record.CampusId = id;
        }
    }

    // ── Small helpers ─────────────────────────────────────────────────────────

    /// <summary>Records a problem.</summary>
    /// <param name="row">The row number, or null for a whole-file problem.</param>
    /// <param name="column">The template column, or null.</param>
    /// <param name="message">The plain-English message.</param>
    private void AddProblem(int? row, string? column, string message) =>
        _problems.Add(new ImportProblem(row, column, message));

    /// <summary>
    /// Splits a semicolon-separated cell into trimmed, non-empty entries.
    /// </summary>
    /// <param name="cell">The cell text.</param>
    /// <returns>The entries in order.</returns>
    private static IEnumerable<string> SplitList(string cell) =>
        cell.Split(';').Select(e => e.Trim()).Where(e => e.Length > 0);

    /// <summary>
    /// Builds the dictionary key for something identified by two names (last and first name;
    /// building and room number). Compared with <see cref="NameMatch.Comparer"/>.
    /// </summary>
    /// <param name="first">The first part.</param>
    /// <param name="second">The second part.</param>
    /// <returns>A key that joins the normalised parts with a separator no name contains.</returns>
    private static string PersonKey(string first, string second) =>
        $"{NameMatch.Normalize(first)}|{NameMatch.Normalize(second)}";

    /// <summary>Writes a count as a word for small numbers ("two"), and as digits otherwise.</summary>
    /// <param name="count">The count.</param>
    private static string CountWord(int count) => count switch
    {
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        _ => count.ToString(),
    };

    /// <summary>Joins items as "A", "A and B" or "A, B and C".</summary>
    /// <param name="items">The items.</param>
    private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
