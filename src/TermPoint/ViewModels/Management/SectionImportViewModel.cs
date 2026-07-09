using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;

#if !BROWSER
using Avalonia.Platform.Storage;
#endif

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Sub-ViewModel for the section portion of the CSV Import flyout.
/// Handles file selection, environment mapping confirmation (rooms, section types,
/// campuses, meeting types), semester/course/instructor resolution, and
/// transactional section import.
/// </summary>
public partial class SectionImportViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainVm;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ICampusRepository _campusRepo;
    private readonly ISchedulingEnvironmentRepository _schedEnvRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly IAcademicYearRepository _academicYearRepo;
    private readonly IDatabaseContext _db;
    private readonly CsvImportParser _parser;
    private readonly CsvImportMatcher _matcher;
    private readonly Action<string> _addLog;

    private List<SectionRow> _parsedRows = new();

    /// <summary>
    /// Pre-resolved semester lookup built during ChooseFile. Key is
    /// (semesterName, academicYearName) normalized to lowercase.
    /// </summary>
    private Dictionary<(string Sem, string Ay), Semester> _semesterLookup = new();

    /// <summary>Maps recognized day tokens to 1=Monday…7=Sunday (same as CsvImportParser).</summary>
    private static readonly Dictionary<string, int> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = 1, ["Monday"] = 1, ["Mon"] = 1, ["Mo"] = 1, ["M"] = 1,
        ["2"] = 2, ["Tuesday"] = 2, ["Tue"] = 2, ["Tu"] = 2, ["T"] = 2,
        ["3"] = 3, ["Wednesday"] = 3, ["Wed"] = 3, ["We"] = 3, ["W"] = 3,
        ["4"] = 4, ["Thursday"] = 4, ["Thu"] = 4, ["Th"] = 4, ["R"] = 4,
        ["5"] = 5, ["Friday"] = 5, ["Fri"] = 5, ["Fr"] = 5, ["F"] = 5,
        ["6"] = 6, ["Saturday"] = 6, ["Sat"] = 6, ["Sa"] = 6,
        ["7"] = 7, ["Sunday"] = 7, ["Sun"] = 7, ["Su"] = 7,
    };

    /// <summary>Short day abbreviations for the meeting summary display.</summary>
    private static readonly string[] DayAbbrev = ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    // ── Observable properties ────────────────────────────────────────────

    /// <summary>Display name of the chosen CSV file.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMappingsCommand))]
    private string? _fileName;

    /// <summary>True after a successful import.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseFileCommand))]
    private bool _isImported;

    /// <summary>True after environment mappings are confirmed — shows the section preview.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMappingsCommand))]
    private bool _isMappingConfirmed;

    /// <summary>Parse or import error text shown as a warning banner.</summary>
    [ObservableProperty] private string? _errorBanner;

    /// <summary>Summary text shown after import completes.</summary>
    [ObservableProperty] private string? _importSummary;

    /// <summary>True when a file has been loaded and mapping tables are populated.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMappingsCommand))]
    private bool _hasFile;

    // ── Mapping collections (one per environment category) ───────────────

    /// <summary>Room mapping rows — one per distinct Room value in the CSV's meeting rows.</summary>
    public ObservableCollection<MappingEntryViewModel> RoomMappings { get; } = new();

    /// <summary>Section type mapping rows.</summary>
    public ObservableCollection<MappingEntryViewModel> SectionTypeMappings { get; } = new();

    /// <summary>Campus mapping rows.</summary>
    public ObservableCollection<MappingEntryViewModel> CampusMappings { get; } = new();

    /// <summary>Meeting type mapping rows.</summary>
    public ObservableCollection<MappingEntryViewModel> MeetingTypeMappings { get; } = new();

    // ── Preview and counts ───────────────────────────────────────────────

    /// <summary>Section preview rows — populated after mappings are confirmed.</summary>
    public ObservableCollection<SectionPreviewRow> PreviewRows { get; } = new();

    [ObservableProperty] private int _newCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _warningCount;

    public SectionImportViewModel(
        MainWindowViewModel mainVm,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IRoomRepository roomRepo,
        ICampusRepository campusRepo,
        ISchedulingEnvironmentRepository schedEnvRepo,
        ISemesterRepository semesterRepo,
        IAcademicYearRepository academicYearRepo,
        IDatabaseContext db,
        CsvImportParser parser,
        CsvImportMatcher matcher,
        Action<string> addLog)
    {
        _mainVm = mainVm;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _campusRepo = campusRepo;
        _schedEnvRepo = schedEnvRepo;
        _semesterRepo = semesterRepo;
        _academicYearRepo = academicYearRepo;
        _db = db;
        _parser = parser;
        _matcher = matcher;
        _addLog = addLog;
    }

    // ── ChooseFile ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens a file picker, parses the section CSV, resolves semesters, and
    /// builds the four environment mapping tables for operator confirmation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChooseFile))]
    private async Task ChooseFile()
    {
#if !BROWSER
        var window = _mainVm.MainWindowReference;
        if (window is null) return;

        await Task.Yield();

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select section CSV",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CSV files") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        FileName = Path.GetFileName(path);
        ErrorBanner = null;
        RoomMappings.Clear();
        SectionTypeMappings.Clear();
        CampusMappings.Clear();
        MeetingTypeMappings.Clear();
        PreviewRows.Clear();
        IsMappingConfirmed = false;
        _parsedRows.Clear();
        _semesterLookup.Clear();

        string csvText;
        try
        {
            csvText = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            ErrorBanner = $"Could not read file: {ex.Message}";
            return;
        }

        var parseResult = _parser.ParseSections(csvText);

        if (parseResult.Errors.Count > 0)
        {
            var errorLines = parseResult.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}");
            ErrorBanner = string.Join("\n", errorLines);
        }

        if (parseResult.Rows.Count == 0 && parseResult.Errors.Count > 0)
            return;

        _parsedRows = parseResult.Rows;

        // ── Build semester lookup ────────────────────────────────────────
        BuildSemesterLookup();

        // ── Build environment mapping tables ─────────────────────────────
        BuildRoomMappings();
        BuildSectionTypeMappings();
        BuildCampusMappings();
        BuildMeetingTypeMappings();

        HasFile = true;

        _addLog($"Loaded {_parsedRows.Count} section(s) from {FileName}" +
                (parseResult.Errors.Count > 0 ? $" ({parseResult.Errors.Count} parse error(s))" : ""));
#endif
    }

    private bool CanChooseFile() => !IsImported;

    /// <summary>
    /// Builds a lookup from CSV semester text → Semester record. Handles two
    /// common CSV formats: bare name ("Fall") and name-with-year ("Fall 2026").
    /// When both Semester and AcademicYear columns are populated, the AY narrows
    /// the match. When only a semester name is given, it must be unambiguous.
    /// </summary>
    private void BuildSemesterLookup()
    {
        var semesters = _semesterRepo.GetAll();
        var academicYears = _academicYearRepo.GetAll();
        var ayById = academicYears.ToDictionary(ay => ay.Id);

        // Index 1: (semesterName, ayName) — both exact.
        var semByKey = new Dictionary<(string, string), Semester>();
        // Index 2: "semesterName startYear" composite (e.g. "fall 2026") for
        // CSVs that embed the year in the Semester column instead of using
        // a separate AcademicYear column.
        var semByNameYear = new Dictionary<string, Semester>(StringComparer.OrdinalIgnoreCase);
        // Index 3: bare semester name (for rows with blank AY, unambiguous).
        var semByNameOnly = new Dictionary<string, Semester?>(StringComparer.OrdinalIgnoreCase);

        foreach (var sem in semesters)
        {
            var nameKey = sem.Name.Trim().ToLowerInvariant();
            semByNameOnly.TryAdd(nameKey, sem);
            if (semByNameOnly.ContainsKey(nameKey) && semByNameOnly[nameKey]?.Id != sem.Id)
                semByNameOnly[nameKey] = null; // ambiguous

            if (!ayById.TryGetValue(sem.AcademicYearId, out var ay)) continue;

            var ayName = ay.Name.Trim().ToLowerInvariant();
            semByKey.TryAdd((nameKey, ayName), sem);

            // Composite: "Fall 2026" where 2026 is the AY start year.
            var compositeKey = $"{nameKey} {ay.StartYear}";
            semByNameYear.TryAdd(compositeKey, sem);
        }

        // For each distinct (Semester, AcademicYear) pair in the CSV, resolve it.
        var pairs = _parsedRows
            .Select(r => (Sem: r.Semester.Trim(), Ay: r.AcademicYear.Trim()))
            .Distinct();

        foreach (var (sem, ay) in pairs)
        {
            var semLower = sem.ToLowerInvariant();
            var ayLower = ay.ToLowerInvariant();
            var lookupKey = (semLower, ayLower);

            // Try 1: exact (semesterName, ayName) match.
            if (!string.IsNullOrEmpty(ay) && semByKey.TryGetValue(lookupKey, out var match))
            {
                _semesterLookup[lookupKey] = match;
                continue;
            }

            // Try 2: CSV semester is "Fall 2026" — try as composite key.
            if (semByNameYear.TryGetValue(semLower, out match))
            {
                _semesterLookup[lookupKey] = match;
                continue;
            }

            // Try 3: strip trailing year from CSV semester, then match
            // bare name against AY. e.g. "Fall 2026" + AY "2026-2027"
            // → look up ("fall", "2026-2027").
            var stripped = StripTrailingYear(semLower);
            if (stripped != semLower)
            {
                if (!string.IsNullOrEmpty(ay) && semByKey.TryGetValue((stripped, ayLower), out match))
                {
                    _semesterLookup[lookupKey] = match;
                    continue;
                }
                // Try bare stripped name.
                if (semByNameOnly.TryGetValue(stripped, out match) && match is not null)
                {
                    _semesterLookup[lookupKey] = match;
                    continue;
                }
            }

            // Try 4: bare name only (no AY), must be unambiguous.
            if (semByNameOnly.TryGetValue(semLower, out match) && match is not null)
                _semesterLookup[lookupKey] = match;
        }
    }

    /// <summary>
    /// Strips a trailing 4-digit year from a semester text, e.g.
    /// "Fall 2026" → "fall", "Early Summer 2025" → "early summer".
    /// Returns the original string unchanged if no trailing year is found.
    /// </summary>
    private static string StripTrailingYear(string text)
    {
        if (text.Length < 5) return text;
        var lastSpace = text.LastIndexOf(' ');
        if (lastSpace <= 0) return text;
        var tail = text[(lastSpace + 1)..];
        if (tail.Length == 4 && int.TryParse(tail, out _))
            return text[..lastSpace].Trim();
        return text;
    }

    /// <summary>
    /// Resolves the semester for a given SectionRow using the pre-built lookup.
    /// Returns null if the semester could not be resolved.
    /// </summary>
    private Semester? ResolveSemester(SectionRow row)
    {
        var semKey = row.Semester.Trim().ToLowerInvariant();
        var ayKey = row.AcademicYear.Trim().ToLowerInvariant();

        if (_semesterLookup.TryGetValue((semKey, ayKey), out var sem))
            return sem;

        return null;
    }

    private void BuildRoomMappings()
    {
        var rooms = _roomRepo.GetAll();
        var roomOptions = rooms
            .Select(r => new EnvironmentTarget
            {
                Id = r.Id,
                DisplayName = $"{r.Building} {r.RoomNumber}".Trim()
            })
            .ToList();

        var distinctRooms = _parsedRows
            .SelectMany(r => r.Meetings)
            .Select(m => m.Room.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);

        foreach (var csvValue in distinctRooms)
        {
            var match = _matcher.MatchEnvironmentValue(csvValue, roomOptions);
            RoomMappings.Add(new MappingEntryViewModel(csvValue, match, roomOptions));
        }
    }

    private void BuildSectionTypeMappings()
    {
        var sectionTypes = _schedEnvRepo.GetAll(SchedulingEnvironmentTypes.SectionType);
        var options = sectionTypes
            .Select(v => new EnvironmentTarget { Id = v.Id, DisplayName = v.Name })
            .ToList();

        var distinct = _parsedRows
            .Select(r => r.SectionType.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);

        foreach (var csvValue in distinct)
        {
            var match = _matcher.MatchEnvironmentValue(csvValue, options);
            SectionTypeMappings.Add(new MappingEntryViewModel(csvValue, match, options));
        }
    }

    private void BuildCampusMappings()
    {
        var campuses = _campusRepo.GetAll();
        var options = campuses
            .Select(c => new EnvironmentTarget { Id = c.Id, DisplayName = c.Name })
            .ToList();

        var distinct = _parsedRows
            .Select(r => r.Campus.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);

        foreach (var csvValue in distinct)
        {
            var match = _matcher.MatchEnvironmentValue(csvValue, options);
            CampusMappings.Add(new MappingEntryViewModel(csvValue, match, options));
        }
    }

    private void BuildMeetingTypeMappings()
    {
        var meetingTypes = _schedEnvRepo.GetAll(SchedulingEnvironmentTypes.MeetingType);
        var options = meetingTypes
            .Select(v => new EnvironmentTarget { Id = v.Id, DisplayName = v.Name })
            .ToList();

        var distinct = _parsedRows
            .SelectMany(r => r.Meetings)
            .Select(m => m.MeetingType.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);

        foreach (var csvValue in distinct)
        {
            var match = _matcher.MatchEnvironmentValue(csvValue, options);
            MeetingTypeMappings.Add(new MappingEntryViewModel(csvValue, match, options));
        }
    }

    // ── ConfirmMappings ──────────────────────────────────────────────────

    /// <summary>
    /// Locks environment mappings, resolves all section references (semester,
    /// course, instructors, schedule), and builds the preview list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirmMappings))]
    private void ConfirmMappings()
    {
        PreviewRows.Clear();

        // Build mapping lookups from the confirmed ComboBox selections.
        var roomMap = BuildMappingLookup(RoomMappings);
        var sectionTypeMap = BuildMappingLookup(SectionTypeMappings);
        var campusMap = BuildMappingLookup(CampusMappings);
        var meetingTypeMap = BuildMappingLookup(MeetingTypeMappings);

        // Load rooms for RoomTypeId carry-over.
        var rooms = _roomRepo.GetAll();
        var roomsById = rooms.ToDictionary(r => r.Id);

        // Build course index for matching. Keyed by whitespace-stripped calendar
        // code so "CHEM 101" (CSV) matches "CHEM101" (DB) automatically.
        var existingCourses = _courseRepo.GetAll();
        var courseIndex = new Dictionary<string, Course>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existingCourses)
        {
            var key = StripAllWhitespace(c.CalendarCode);
            courseIndex.TryAdd(key, c);
        }

        // Build instructor index for matching.
        var existingInstructors = _instructorRepo.GetAll();
        var instructorIndex = _matcher.BuildInstructorIndex(existingInstructors);

        foreach (var row in _parsedRows)
        {
            var warnings = new List<string>();
            string? rejectionReason = null;

            // ── Semester resolution ──────────────────────────────────
            var semester = ResolveSemester(row);
            if (semester is null)
            {
                var ayPart = string.IsNullOrEmpty(row.AcademicYear.Trim())
                    ? "" : $" / {row.AcademicYear.Trim()}";
                rejectionReason = $"no semester '{row.Semester.Trim()}{ayPart}'";
            }

            // ── Course resolution (whitespace-insensitive) ─────────
            string? courseId = null;
            string? courseLevel = null;
            var csvCourseKey = StripAllWhitespace(row.CourseCode);
            if (string.IsNullOrEmpty(csvCourseKey))
            {
                if (rejectionReason is null)
                    rejectionReason = "blank CourseCode";
            }
            else if (courseIndex.TryGetValue(csvCourseKey, out var matchedCourse))
            {
                courseId = matchedCourse.Id;
                courseLevel = !string.IsNullOrEmpty(matchedCourse.Level)
                    ? matchedCourse.Level
                    : CourseLevelParser.ParseLevel(csvCourseKey);
            }
            else if (rejectionReason is null)
            {
                rejectionReason = $"no course '{row.CourseCode.Trim()}'";
            }

            // ── Duplicate section code check ─────────────────────────
            if (rejectionReason is null && semester is not null && courseId is not null)
            {
                if (_sectionRepo.ExistsBySectionCode(semester.Id, courseId, row.SectionCode.Trim(), null))
                {
                    rejectionReason = "duplicate section code";
                }
            }

            // ── Instructor resolution ────────────────────────────────
            var resolvedInstructors = new List<InstructorAssignment>();
            var instructorDisplay = ResolveInstructors(
                row.Instructors, instructorIndex, resolvedInstructors, warnings);

            // ── Schedule construction ────────────────────────────────
            var resolvedSchedule = BuildSchedule(
                row.Meetings, roomMap, meetingTypeMap, roomsById, warnings);

            // ── Environment mapping ──────────────────────────────────
            // SectionTypeId and CampusId are resolved but stored on the
            // preview row indirectly through Row + the mapping lookups —
            // they'll be read back from the lookups at Import time.

            var meetingSummary = FormatMeetingSummary(resolvedSchedule);

            PreviewRows.Add(new SectionPreviewRow(
                row,
                semester?.Id,
                courseId,
                courseLevel,
                instructorDisplay,
                meetingSummary,
                resolvedInstructors,
                resolvedSchedule,
                warnings,
                rejectionReason));
        }

        IsMappingConfirmed = true;
        _mappingLookups = (roomMap, sectionTypeMap, campusMap, meetingTypeMap);
        RecalculateCounts();

        _addLog($"Mappings confirmed. {NewCount} importable, {RejectedCount} rejected, {WarningCount} with warnings.");
    }

    private bool CanConfirmMappings() => HasFile && !IsMappingConfirmed;

    /// <summary>Cached mapping lookups, set at ConfirmMappings time, used at Import time.</summary>
    private (Dictionary<string, string?> Room, Dictionary<string, string?> SectionType,
             Dictionary<string, string?> Campus, Dictionary<string, string?> MeetingType) _mappingLookups;

    /// <summary>
    /// Builds a case-insensitive lookup from CSV value → resolved DB record ID (or null if skipped).
    /// </summary>
    private static Dictionary<string, string?> BuildMappingLookup(
        ObservableCollection<MappingEntryViewModel> entries)
    {
        var lookup = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            lookup[entry.CsvValue] = entry.ResolvedTarget?.Id;
        }
        return lookup;
    }

    /// <summary>
    /// Parses the semicolon-separated instructor names from the CSV, matches each
    /// against existing instructors, and populates the resolved assignments list.
    /// Returns a formatted display string.
    /// </summary>
    private string ResolveInstructors(
        string instructorsText,
        InstructorMatchIndex instructorIndex,
        List<InstructorAssignment> resolved,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(instructorsText))
            return "(none)";

        var names = instructorsText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var displayParts = new List<string>();

        foreach (var name in names)
        {
            var (firstName, lastName) = SplitName(name);
            var tempRow = new InstructorRow { FirstName = firstName, LastName = lastName };
            var match = _matcher.MatchInstructor(tempRow, instructorIndex);

            if (match.Status == MatchStatus.Exact && match.Resolved is not null)
            {
                resolved.Add(new InstructorAssignment
                {
                    InstructorId = match.Resolved.Id,
                    Workload = 1.0m
                });
                displayParts.Add(name);
            }
            else
            {
                warnings.Add($"Instructor '{name}' not found");
                displayParts.Add($"{name} ⚠");
            }
        }

        return string.Join("; ", displayParts);
    }

    /// <summary>
    /// Splits a "FirstName LastName" string on the last space. Returns
    /// (firstName, lastName). If there's no space, lastName gets the whole string.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var trimmed = fullName.Trim();
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace <= 0)
            return (string.Empty, trimmed);
        return (trimmed[..lastSpace].Trim(), trimmed[(lastSpace + 1)..].Trim());
    }

    /// <summary>
    /// Converts parsed MeetingRows into SectionDaySchedule entries using the
    /// confirmed environment mapping lookups.
    /// </summary>
    private List<SectionDaySchedule> BuildSchedule(
        List<MeetingRow> meetings,
        Dictionary<string, string?> roomMap,
        Dictionary<string, string?> meetingTypeMap,
        Dictionary<string, Room> roomsById,
        List<string> warnings)
    {
        var schedule = new List<SectionDaySchedule>();

        foreach (var m in meetings)
        {
            if (!DayMap.TryGetValue(m.Day, out var day))
            {
                warnings.Add($"Unrecognized day '{m.Day}'");
                continue;
            }

            if (!TryParseTimeToMinutes(m.StartTime, out var startMinutes))
            {
                warnings.Add($"Unparseable start time '{m.StartTime}'");
                continue;
            }

            if (!int.TryParse(m.DurationMin, out var durationMinutes) || durationMinutes <= 0)
            {
                warnings.Add($"Invalid duration '{m.DurationMin}'");
                continue;
            }

            // Resolve room.
            string? roomId = null;
            string? roomTypeId = null;
            var roomValue = m.Room.Trim();
            if (!string.IsNullOrEmpty(roomValue) && roomMap.TryGetValue(roomValue, out var mappedRoomId))
            {
                roomId = mappedRoomId;
                if (roomId is not null && roomsById.TryGetValue(roomId, out var room))
                    roomTypeId = room.RoomTypeId;
            }

            // Resolve meeting type.
            string? meetingTypeId = null;
            var mtValue = m.MeetingType.Trim();
            if (!string.IsNullOrEmpty(mtValue) && meetingTypeMap.TryGetValue(mtValue, out var mappedMtId))
                meetingTypeId = mappedMtId;

            schedule.Add(new SectionDaySchedule
            {
                Day = day,
                StartMinutes = startMinutes,
                DurationMinutes = durationMinutes,
                RoomId = roomId,
                RoomTypeId = roomTypeId,
                MeetingTypeId = meetingTypeId,
                Frequency = string.IsNullOrWhiteSpace(m.Frequency) ? null : m.Frequency.Trim()
            });
        }

        return schedule;
    }

    /// <summary>Parses "h:mm tt" format to minutes from midnight.</summary>
    private static bool TryParseTimeToMinutes(string text, out int minutes)
    {
        minutes = 0;
        if (!DateTime.TryParseExact(text, "h:mm tt",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;
        minutes = dt.Hour * 60 + dt.Minute;
        return true;
    }

    /// <summary>Formats a schedule list into a compact summary string.</summary>
    private static string FormatMeetingSummary(List<SectionDaySchedule> schedule)
    {
        if (schedule.Count == 0)
            return "(unscheduled)";

        var parts = schedule.Select(s =>
        {
            var dayName = s.Day >= 1 && s.Day <= 7 ? DayAbbrev[s.Day] : $"Day{s.Day}";
            var startHour = s.StartMinutes / 60;
            var startMin = s.StartMinutes % 60;
            var amPm = startHour >= 12 ? "PM" : "AM";
            var displayHour = startHour > 12 ? startHour - 12 : (startHour == 0 ? 12 : startHour);
            var timeStr = $"{displayHour}:{startMin:D2} {amPm}";
            var freq = SectionDaySchedule.FormatFrequency(s.Frequency);
            return $"{dayName} {timeStr} ({s.DurationMinutes} min){(string.IsNullOrEmpty(freq) ? "" : " " + freq)}";
        });

        return string.Join("; ", parts);
    }

    // ── Import ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates Section entities from the preview rows and inserts them into the
    /// database. Wraps all inserts in a single transaction.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        var created = 0;
        var skipped = 0;
        var rejected = 0;

        var (_, sectionTypeMap, campusMap, _) = _mappingLookups;

        void DoInserts(System.Data.Common.DbTransaction? tx)
        {
            foreach (var preview in PreviewRows)
            {
                if (preview.IsRejected)
                {
                    rejected++;
                    continue;
                }

                // Resolve section-level environment mappings.
                string? sectionTypeId = null;
                var stValue = preview.Row.SectionType.Trim();
                if (!string.IsNullOrEmpty(stValue) && sectionTypeMap.TryGetValue(stValue, out var stId))
                    sectionTypeId = stId;

                string? campusId = null;
                var campValue = preview.Row.Campus.Trim();
                if (!string.IsNullOrEmpty(campValue) && campusMap.TryGetValue(campValue, out var cId))
                    campusId = cId;

                var section = new Section
                {
                    Id = Guid.NewGuid().ToString(),
                    SemesterId = preview.SemesterId!,
                    CourseId = preview.CourseId!,
                    SectionCode = preview.SectionCode,
                    Level = preview.CourseLevel,
                    SectionTypeId = sectionTypeId,
                    CampusId = campusId,
                    InstructorAssignments = preview.ResolvedInstructors,
                    Schedule = preview.ResolvedSchedule,
                    Notes = string.Empty,
                    TagIds = new List<string>(),
                    ResourceIds = new List<string>(),
                    Reserves = new List<SectionReserve>(),
                    Flag = SectionFlag.None
                };

                _sectionRepo.Insert(section, tx);
                created++;
            }
        }

        try
        {
            if (_db.SupportsTransactions)
            {
                using var tx = _db.Connection.BeginTransaction();
                try
                {
                    DoInserts(tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            else
            {
                DoInserts(null);
            }

            IsImported = true;
            ImportSummary = $"Imported {created} new, {rejected} rejected.";
            _addLog($"Section import complete: {created} created, {rejected} rejected.");
        }
        catch (Exception ex)
        {
            ErrorBanner = $"Import failed: {ex.Message}";
            _addLog($"Section import FAILED: {ex.Message}");
        }
    }

    private bool CanImport() => IsMappingConfirmed && !IsImported;

    private void RecalculateCounts()
    {
        NewCount = PreviewRows.Count(r => !r.IsRejected);
        RejectedCount = PreviewRows.Count(r => r.IsRejected);
        WarningCount = PreviewRows.Count(r => r.HasWarnings);
    }

    private static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Removes all whitespace characters from a string (e.g. "CHEM 101" → "CHEM101").</summary>
    private static string StripAllWhitespace(string? value) =>
        string.Concat((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
}
