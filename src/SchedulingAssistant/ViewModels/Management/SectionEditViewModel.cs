using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Wrapper used by the Section Prefix picker ComboBox in the section editor.
/// The sentinel item (<see cref="Prefix"/> == null) represents "no prefix selected".
/// </summary>
public record SectionPrefixPickerItem(SectionPrefix? Prefix, string Label);

public partial class SectionEditViewModel : ViewModelBase
{
    [ObservableProperty] private string? _selectedCourseId;
    [ObservableProperty] private string _sectionCode = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private ObservableCollection<SectionMeetingViewModel> _meetings = new();
    [ObservableProperty] private ObservableCollection<Course> _courses;

    // Subject and filtered course numbers
    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSectionCodeEnabled))]
    [NotifyPropertyChangedFor(nameof(HasAvailableCourseNumbers))]
    private Subject? _selectedSubject;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSectionCodeEnabled))]
    [NotifyPropertyChangedFor(nameof(HasAvailableCourseNumbers))]
    private ObservableCollection<string> _courseNumbers = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSectionCodeEnabled))]
    private string? _selectedCourseNumber;

    // Section Prefix picker — optional shortcut that auto-suggests the next available code
    [ObservableProperty] private List<SectionPrefixPickerItem> _prefixOptions = new();
    [ObservableProperty] private SectionPrefixPickerItem? _selectedPrefixOption;

    // ── Step-gate state ──────────────────────────────────────────────────────

    /// <summary>True once a course is selected; enables the Section Code field.</summary>
    public bool IsSectionCodeEnabled => !string.IsNullOrEmpty(SelectedCourseId);

    /// <summary>True when a subject is selected and course numbers are available to choose from.</summary>
    public bool HasAvailableCourseNumbers => SelectedSubject is not null && CourseNumbers.Count > 0;

    /// <summary>
    /// True when course is selected, section code is non-empty, and the code has been
    /// validated as unique (by CommitSectionCode on LostFocus or at construction).
    /// Purely derived from the validated course+code snapshot.
    /// </summary>
    public bool AreOtherFieldsEnabled =>
        !string.IsNullOrEmpty(SelectedCourseId)
        && !string.IsNullOrEmpty(SectionCode.Trim())
        && SectionCodeError == null
        && _validatedCourseId == SelectedCourseId
        && string.Equals(_validatedSectionCode, SectionCode.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Error message shown beneath Section Code; null when no error.</summary>
    [ObservableProperty] private string? _sectionCodeError;

    /// <summary>Snapshot of the course+code pair that was last validated successfully.</summary>
    private string? _validatedCourseId;
    private string? _validatedSectionCode;

    private readonly Func<string, string, bool> _isSectionCodeDuplicate;

    // Instructor multi-select
    [ObservableProperty] private ObservableCollection<InstructorSelectionViewModel> _instructorSelections = new();

    // Section-level single-select property collections (include a leading "(none)" sentinel)
    [ObservableProperty] private ObservableCollection<SectionPropertyValue> _sectionTypes = new();

    // Section-level single-select selections (empty string = null / none)
    [ObservableProperty] private string? _selectedSectionTypeId;
    [ObservableProperty] private string? _selectedCampusId;

    // Multi-select collections
    [ObservableProperty] private ObservableCollection<TagSelectionViewModel> _tagSelections = new();
    [ObservableProperty] private ObservableCollection<ResourceSelectionViewModel> _resourceSelections = new();
    [ObservableProperty] private ObservableCollection<ReserveSelectionViewModel> _reserveSelections = new();

    /// <summary>Comma-joined names (with workload) of selected instructors, for the Expander header.</summary>
    public string InstructorSummary
    {
        get
        {
            var parts = InstructorSelections
                .Where(i => i.IsSelected)
                .Select(i =>
                {
                    var w = i.ParsedWorkload;
                    return w.HasValue ? $"{i.DisplayName} [{w.Value:0.##}]" : i.DisplayName;
                })
                .ToList();
            return parts.Count > 0 ? string.Join(", ", parts) : "(none)";
        }
    }

    /// <summary>Comma-joined names of selected tags, for the Expander header.</summary>
    public string TagSummary =>
        TagSelections.Where(t => t.IsSelected).Select(t => t.Value.Name) is var names && names.Any()
            ? string.Join(", ", names)
            : "(none)";

    /// <summary>Comma-joined names of selected resources, for the Expander header.</summary>
    public string ResourceSummary =>
        ResourceSelections.Where(r => r.IsSelected).Select(r => r.Value.Name) is var names && names.Any()
            ? string.Join(", ", names)
            : "(none)";

    /// <summary>Summary of selected reserves with their counts, for the Expander header.</summary>
    public string ReserveSummary
    {
        get
        {
            var parts = ReserveSelections
                .Where(r => r.IsSelected)
                .Select(r =>
                {
                    var code = r.ParsedCode;
                    return code.HasValue ? $"{r.Value.Name}:{code.Value}" : r.Value.Name;
                })
                .ToList();
            return parts.Count > 0 ? string.Join(", ", parts) : "(none)";
        }
    }

    public string FormTitle => IsNew ? "Add Section" : "Edit Section";
    public bool IsNew { get; }

    private readonly Section _section;
    private readonly Func<Section, Task> _onSave;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly IReadOnlyList<SectionPropertyValue> _meetingTypes;
    private readonly IReadOnlyList<Room> _rooms;
    private readonly bool _includeSaturday;
    private readonly double? _defaultBlockLength;
    private readonly IReadOnlyList<Subject> _allSubjects;

    /// <summary>
    /// True once the constructor has finished. Used to suppress course-tag merging
    /// during construction for existing sections (where tags are already populated),
    /// while still allowing it after construction when the user changes the course.
    /// </summary>
    private bool _isConstructed;

    // ── Pattern coupling state ─────────────────────────────────────────────────
    // After ApplyPattern creates meetings, the lead meeting's first change to each
    // of these three fields is propagated once to all follower meetings, then decoupled.

    private SectionMeetingViewModel? _couplingLeader;
    private List<SectionMeetingViewModel> _couplingFollowers = new();
    private HashSet<string> _couplingRemainingFields = new();
    private PropertyChangedEventHandler? _couplingHandler;

    // ── Block-pattern shortcuts ───────────────────────────────────────────────

    private readonly BlockPattern? _pattern1;
    private readonly BlockPattern? _pattern2;
    private readonly BlockPattern? _pattern3;
    private readonly BlockPattern? _pattern4;
    private readonly BlockPattern? _pattern5;

    /// <summary>Label for the "Apply" button for Pattern 1 (null when no pattern is saved).</summary>
    public string? Pattern1Label => _pattern1 is { Name.Length: > 0 } p ? p.Name : null;

    /// <summary>Label for the "Apply" button for Pattern 2 (null when no pattern is saved).</summary>
    public string? Pattern2Label => _pattern2 is { Name.Length: > 0 } p ? p.Name : null;

    /// <summary>Label for the "Apply" button for Pattern 3 (null when no pattern is saved).</summary>
    public string? Pattern3Label => _pattern3 is { Name.Length: > 0 } p ? p.Name : null;

    /// <summary>Label for the "Apply" button for Pattern 4 (null when no pattern is saved).</summary>
    public string? Pattern4Label => _pattern4 is { Name.Length: > 0 } p ? p.Name : null;

    /// <summary>Label for the "Apply" button for Pattern 5 (null when no pattern is saved).</summary>
    public string? Pattern5Label => _pattern5 is { Name.Length: > 0 } p ? p.Name : null;

    /// <summary>True when Pattern 1 is configured and can be applied.</summary>
    public bool HasPattern1 => _pattern1 is { Days.Count: > 0 };

    /// <summary>True when Pattern 2 is configured and can be applied.</summary>
    public bool HasPattern2 => _pattern2 is { Days.Count: > 0 };

    /// <summary>True when Pattern 3 is configured and can be applied.</summary>
    public bool HasPattern3 => _pattern3 is { Days.Count: > 0 };

    /// <summary>True when Pattern 4 is configured and can be applied.</summary>
    public bool HasPattern4 => _pattern4 is { Days.Count: > 0 };

    /// <summary>True when Pattern 5 is configured and can be applied.</summary>
    public bool HasPattern5 => _pattern5 is { Days.Count: > 0 };

    /// <summary>All configured section prefixes, loaded once at construction for prefix matching.</summary>
    private readonly IReadOnlyList<SectionPrefix> _prefixes;

    /// <summary>
    /// Set by the view to close the hosting window when Save or Cancel is invoked.
    /// </summary>
    public Action? RequestClose { get; set; }

    // ── Step-gate partial callbacks ──────────────────────────────────────────
    // These only clear stale errors and re-raise the computed properties.
    // AreOtherFieldsEnabled compares the live values against the validated snapshot,
    // so no mutable flag needs to be maintained here.

    partial void OnSectionCodeErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(AreOtherFieldsEnabled));
    }

    partial void OnSelectedCourseIdChanged(string? value)
    {
        SectionCodeError = null;
        SelectedPrefixOption = null;
        OnPropertyChanged(nameof(IsSectionCodeEnabled));
        OnPropertyChanged(nameof(AreOtherFieldsEnabled));

        // Post-construction course changes (including initial selection on a new section):
        // merge the new course's tags into the tag selections without removing any
        // tags the user may have already chosen.
        if (_isConstructed && !string.IsNullOrEmpty(value))
            MergeCourseTags(value);
    }

    partial void OnSelectedSubjectChanged(Subject? value)
    {
        // Update course numbers when subject changes
        SelectedCourseNumber = null;
        if (value is not null)
        {
            var courseNumbers = Courses
                .Where(c => c.SubjectId == value.Id)
                .Where(c => c.CalendarCode.Length >= value.CalendarAbbreviation.Length)
                .Select(c => c.CalendarCode[value.CalendarAbbreviation.Length..])
                .OrderBy(n => n)
                .Distinct()
                .ToList();
            CourseNumbers = new ObservableCollection<string>(courseNumbers);
        }
        else
        {
            CourseNumbers = new ObservableCollection<string>();
        }
        OnPropertyChanged(nameof(IsSectionCodeEnabled));
    }

    partial void OnSelectedCourseNumberChanged(string? value)
    {
        // Update SelectedCourseId based on selected subject and course number
        if (!string.IsNullOrEmpty(value) && SelectedSubject is not null)
        {
            var calendarCode = $"{SelectedSubject.CalendarAbbreviation}{value}";
            var course = Courses.FirstOrDefault(c =>
                c.CalendarCode.Equals(calendarCode, StringComparison.OrdinalIgnoreCase));
            if (course is not null)
            {
                SelectedCourseId = course.Id;
            }
        }
        else
        {
            SelectedCourseId = null;
        }
    }

    partial void OnSelectedPrefixOptionChanged(SectionPrefixPickerItem? value)
    {
        // Guard: do nothing when cleared (e.g. on course change) or no course yet selected.
        if (value?.Prefix is null || string.IsNullOrEmpty(SelectedCourseId))
            return;

        // Find the first available code for this prefix in the current course+semester.
        // DesignatorType must be forwarded so Letter prefixes generate letter codes (e.g.
        // "TUTA") and Number prefixes generate numeric codes (e.g. "AB1").
        var next = SectionPrefixHelper.FindNextAvailableCode(
            value.Prefix.Prefix,
            code => _isSectionCodeDuplicate(SelectedCourseId, code),
            value.Prefix.DesignatorType);

        if (next is null) return; // all 1-999 slots taken

        SectionCode = next;
        CommitSectionCode();
    }

    partial void OnSectionCodeChanged(string value)
    {
        SectionCodeError = null;
        OnPropertyChanged(nameof(AreOtherFieldsEnabled));
    }

    /// <summary>
    /// Called when the Section Code field loses focus (hooked in the view's code-behind).
    /// Validates uniqueness and, if valid, records the validated course+code pair so that
    /// <see cref="AreOtherFieldsEnabled"/> becomes true.
    /// </summary>
    public void CommitSectionCode()
    {
        var code = SectionCode.Trim();
        if (string.IsNullOrEmpty(SelectedCourseId) || string.IsNullOrEmpty(code))
        {
            OnPropertyChanged(nameof(AreOtherFieldsEnabled));
            return;
        }

        if (_isSectionCodeDuplicate(SelectedCourseId, code))
        {
            SectionCodeError = "A section with this code already exists for this course in the selected semester.";
            _validatedCourseId = null;
            _validatedSectionCode = null;
        }
        else
        {
            SectionCodeError = null;
            _validatedCourseId = SelectedCourseId;
            _validatedSectionCode = code;

            // Auto-set (or clear) campus based on whether the code starts with a
            // known section prefix.  This runs on both manual entry and picker-driven
            // auto-fill so the campus is always consistent with the committed code.
            var matched = SectionPrefixHelper.MatchPrefix(code, _prefixes);
            SelectedCampusId = matched?.CampusId ?? "";
        }
        OnPropertyChanged(nameof(AreOtherFieldsEnabled));
    }

    /// <summary>
    /// Constructs the inline section editor view-model.
    /// </summary>
    /// <param name="section">The section being edited (or a blank one for Add).</param>
    /// <param name="isNew">True when creating a new section; false when editing an existing one.</param>
    /// <param name="courses">Active courses available for selection.</param>
    /// <param name="subjects">All subjects, used to populate the Subject dropdown.</param>
    /// <param name="instructors">All instructors for the multi-select panel.</param>
    /// <param name="rooms">All rooms, passed to each meeting editor.</param>
    /// <param name="legalStartTimes">Valid start times for the academic year.</param>
    /// <param name="includeSaturday">Whether Saturday is shown as a meeting day.</param>
    /// <param name="sectionTypes">All section-type property values.</param>
    /// <param name="meetingTypes">All meeting-type property values.</param>
    /// <param name="campuses">All campus entities (used to build prefix picker labels).</param>
    /// <param name="allTags">All tag property values.</param>
    /// <param name="allResources">All resource property values.</param>
    /// <param name="allReserves">All reserve property values.</param>
    /// <param name="isSectionCodeDuplicate">
    /// Delegate that returns true when a given (courseId, code) pair already exists
    /// in the target semester.  Scoped by the caller to the correct semester.
    /// </param>
    /// <param name="onSave">Async callback invoked when the user saves.</param>
    /// <param name="blockPatternRepository">Repository used to load saved block patterns.</param>
    /// <param name="prefixRepository">Repository used to load section prefixes for code auto-suggest.</param>
    /// <param name="defaultBlockLength">Optional preferred block length pre-filled on new meetings.</param>
    public SectionEditViewModel(
        Section section,
        bool isNew,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<Instructor> instructors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        IReadOnlyList<SectionPropertyValue> sectionTypes,
        IReadOnlyList<SectionPropertyValue> meetingTypes,
        IReadOnlyList<Campus> campuses,
        IReadOnlyList<SectionPropertyValue> allTags,
        IReadOnlyList<SectionPropertyValue> allResources,
        IReadOnlyList<SectionPropertyValue> allReserves,
        Func<string, string, bool> isSectionCodeDuplicate,
        Func<Section, Task> onSave,
        IBlockPatternRepository blockPatternRepository,
        ISectionPrefixRepository prefixRepository,
        double? defaultBlockLength = null)
    {
        _section = section;
        IsNew = isNew;
        _onSave = onSave;
        _legalStartTimes = legalStartTimes;
        _meetingTypes = meetingTypes;
        _rooms = rooms;
        _includeSaturday = includeSaturday;
        _defaultBlockLength = defaultBlockLength;
        _isSectionCodeDuplicate = isSectionCodeDuplicate;

        // Load saved block patterns for the shortcut buttons
        var allPatterns = blockPatternRepository.GetAll();
        _pattern1 = allPatterns.Count > 0 ? allPatterns[0] : null;
        _pattern2 = allPatterns.Count > 1 ? allPatterns[1] : null;
        _pattern3 = allPatterns.Count > 2 ? allPatterns[2] : null;
        _pattern4 = allPatterns.Count > 3 ? allPatterns[3] : null;
        _pattern5 = allPatterns.Count > 4 ? allPatterns[4] : null;

        // Load all configured section prefixes for use by the prefix picker and CommitSectionCode.
        _prefixes = prefixRepository.GetAll();

        // Build campus name lookup so prefix labels can show "AB — Abbotsford".
        var campusNameById = campuses.ToDictionary(c => c.Id, c => c.Name);

        // Build the prefix picker items: sentinel "(none)" first, then one per prefix.
        var pickerItems = new List<SectionPrefixPickerItem> { new(null, "(none)") };
        foreach (var p in _prefixes)
        {
            var label = p.CampusId is not null && campusNameById.TryGetValue(p.CampusId, out var campusName)
                ? $"{p.Prefix} \u2014 {campusName}"   // "AB — Abbotsford"
                : p.Prefix;
            pickerItems.Add(new(p, label));
        }
        PrefixOptions = pickerItems;

        Courses = new ObservableCollection<Course>(courses);
        _allSubjects = subjects;
        Subjects = new ObservableCollection<Subject>(subjects);

        // Initialize subject and course number (for both editing and copying)
        if (!string.IsNullOrEmpty(section.CourseId))
        {
            // Editing or copying: extract from existing course
            var existingCourse = courses.FirstOrDefault(c => c.Id == section.CourseId);
            if (existingCourse is not null)
            {
                var subject = subjects.FirstOrDefault(s => s.Id == existingCourse.SubjectId);
                if (subject is not null)
                {
                    SelectedSubject = subject;
                    // Extract course number from calendar code
                    var courseNumber = existingCourse.CalendarCode.Length >= subject.CalendarAbbreviation.Length
                        ? existingCourse.CalendarCode[subject.CalendarAbbreviation.Length..]
                        : string.Empty;
                    // OnSelectedSubjectChanged will populate CourseNumbers, so we can now set the selection
                    SelectedCourseNumber = courseNumber;
                }
            }
        }
        else if (subjects.Count > 0)
        {
            // New section: pre-select first subject to populate course numbers
            SelectedSubject = subjects[0];
        }

        SelectedCourseId = section.CourseId;
        SectionCode      = section.SectionCode;
        Notes            = section.Notes;

        // If both fields are already populated (edit or copy-with-code), record them as
        // the validated pair so AreOtherFieldsEnabled computes to true immediately.
        // This is immune to Avalonia re-firing setters — the computed property just
        // compares live values against these snapshots.
        if (!string.IsNullOrEmpty(section.CourseId) && !string.IsNullOrEmpty(section.SectionCode))
        {
            _validatedCourseId = section.CourseId;
            _validatedSectionCode = section.SectionCode;
        }

        // The prefix picker always starts in the "(none)" state. It is a generation aid only —
        // the authoritative campus is SelectedCampusId, which is set from section data on load
        // and updated programmatically by CommitSectionCode via SectionPrefixHelper.MatchPrefix.
        SelectedPrefixOption = null;

        // Instructor multi-select with workload
        InstructorSelections = new ObservableCollection<InstructorSelectionViewModel>(
            instructors.Select(i =>
            {
                var assignment = section.InstructorAssignments.FirstOrDefault(a => a.InstructorId == i.Id);
                return new InstructorSelectionViewModel(i, assignment is not null, assignment?.Workload);
            }));
        WireSelectionSummary(InstructorSelections, nameof(InstructorSummary));

        // Build single-select lists with a leading "(none)" sentinel
        SectionTypes = BuildSentinelList(sectionTypes);

        SelectedSectionTypeId = section.SectionTypeId ?? "";
        SelectedCampusId      = section.CampusId      ?? "";

        // Build multi-select lists
        TagSelections = new ObservableCollection<TagSelectionViewModel>(
            allTags.Select(t => new TagSelectionViewModel(t, section.TagIds.Contains(t.Id))));
        WireSelectionSummary(TagSelections, nameof(TagSummary));

        ResourceSelections = new ObservableCollection<ResourceSelectionViewModel>(
            allResources.Select(r => new ResourceSelectionViewModel(r, section.ResourceIds.Contains(r.Id))));
        WireSelectionSummary(ResourceSelections, nameof(ResourceSummary));

        ReserveSelections = new ObservableCollection<ReserveSelectionViewModel>(
            allReserves.Select(r =>
            {
                var existing = section.Reserves.FirstOrDefault(x => x.ReserveId == r.Id);
                return new ReserveSelectionViewModel(r, existing?.Code);
            }));
        WireReserveSummary();

        // Meetings — pass rooms down so each meeting can show its own room picker.
        // defaultBlockLength is passed but has no effect on existing meetings (only new ones).
        foreach (var entry in section.Schedule)
            Meetings.Add(new SectionMeetingViewModel(legalStartTimes, includeSaturday, meetingTypes, rooms, entry, defaultBlockLength));

        // Mark construction complete so OnSelectedCourseIdChanged can merge tags going forward.
        _isConstructed = true;

        // For new sections (Add or Copy) that already have a course pre-set, immediately merge
        // the course's tags. On Copy the section may already have tags; MergeCourseTags only
        // adds missing ones.
        if (isNew && !string.IsNullOrEmpty(SelectedCourseId))
            MergeCourseTags(SelectedCourseId);
    }

    /// <summary>
    /// Wires up collection + per-item PropertyChanged so that the named summary property
    /// fires OnPropertyChanged whenever any IsSelected (or WorkloadText for instructors) changes.
    /// </summary>
    private void WireSelectionSummary<T>(ObservableCollection<T> collection, string summaryPropertyName)
        where T : INotifyPropertyChanged
    {
        foreach (var item in collection)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is "IsSelected" or "WorkloadText")
                    OnPropertyChanged(summaryPropertyName);
            };

        collection.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (T item in e.NewItems)
                    item.PropertyChanged += (_, pe) =>
                    {
                        if (pe.PropertyName is "IsSelected" or "WorkloadText")
                            OnPropertyChanged(summaryPropertyName);
                    };
            OnPropertyChanged(summaryPropertyName);
        };
    }

    /// <summary>
    /// Wires ReserveSelections so ReserveSummary updates on IsSelected or CodeText changes.
    /// </summary>
    private void WireReserveSummary()
    {
        foreach (var item in ReserveSelections)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is "IsSelected" or "CodeText")
                    OnPropertyChanged(nameof(ReserveSummary));
            };

        ReserveSelections.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (ReserveSelectionViewModel item in e.NewItems)
                    item.PropertyChanged += (_, pe) =>
                    {
                        if (pe.PropertyName is "IsSelected" or "CodeText")
                            OnPropertyChanged(nameof(ReserveSummary));
                    };
            OnPropertyChanged(nameof(ReserveSummary));
        };
    }

    private static ObservableCollection<SectionPropertyValue> BuildSentinelList(
        IReadOnlyList<SectionPropertyValue> values)
    {
        var list = new List<SectionPropertyValue>
            { new SectionPropertyValue { Id = "", Name = "(none)" } };
        list.AddRange(values);
        return new ObservableCollection<SectionPropertyValue>(list);
    }

    [RelayCommand]
    private void AddMeeting()
    {
        Meetings.Add(new SectionMeetingViewModel(_legalStartTimes, _includeSaturday, _meetingTypes, _rooms,
            defaultBlockLength: _defaultBlockLength));
    }

    [RelayCommand]
    private void RemoveMeeting(SectionMeetingViewModel meeting)
    {
        // Keep coupling state consistent when meetings are manually removed.
        if (meeting == _couplingLeader)
            TearDownPatternCoupling();     // Leader gone — coupling has no source; discard entirely.
        else
            _couplingFollowers.Remove(meeting); // Remove from propagation targets if it was a follower.

        Meetings.Remove(meeting);
    }

    [RelayCommand]
    private void ApplyPattern1() => ApplyPattern(_pattern1);

    [RelayCommand]
    private void ApplyPattern2() => ApplyPattern(_pattern2);

    [RelayCommand]
    private void ApplyPattern3() => ApplyPattern(_pattern3);

    [RelayCommand]
    private void ApplyPattern4() => ApplyPattern(_pattern4);

    [RelayCommand]
    private void ApplyPattern5() => ApplyPattern(_pattern5);

    /// <summary>
    /// Replaces the current meeting list with one blank meeting per day in the pattern.
    /// The new meetings start with no block length, start time, or meeting type pre-filled.
    /// After creation, the meetings are coupled: the first time the user sets
    /// <c>SelectedBlockLength</c>, <c>SelectedStartTime</c>, or <c>SelectedMeetingTypeId</c>
    /// on the lead (first) meeting, that value is propagated once to all other meetings.
    /// After a field has propagated, it decouples permanently — subsequent changes to either
    /// meeting do not affect the other.
    /// </summary>
    private void ApplyPattern(BlockPattern? pattern)
    {
        if (pattern is null || pattern.Days.Count == 0) return;

        TearDownPatternCoupling();
        Meetings.Clear();

        var created = new List<SectionMeetingViewModel>();
        foreach (var day in pattern.Days.Order())
        {
            // Pre-fill the preferred block length (if set in Settings) via the constructor's
            // backing-field path, so no PropertyChanged fires and the SelectedBlockLength
            // coupling slot is not consumed — the user can still change it on the leader
            // and have it propagate to the other meetings.
            var meeting = new SectionMeetingViewModel(_legalStartTimes, _includeSaturday, _meetingTypes, _rooms,
                defaultBlockLength: _defaultBlockLength);
            meeting.SelectedDay = day;
            created.Add(meeting);
            Meetings.Add(meeting);
        }

        if (created.Count > 1)
            SetupPatternCoupling(created[0], created.Skip(1).ToList());
    }

    /// <summary>
    /// Subscribes to PropertyChanged on <paramref name="leader"/> and, for each of the three
    /// coupled fields, propagates the leader's new value to every follower the first time that
    /// field changes. Once all three fields have fired, the subscription is removed.
    /// </summary>
    /// <param name="leader">The first meeting in the pattern group — the source of propagation.</param>
    /// <param name="followers">All other meetings in the group — the propagation targets.</param>
    private void SetupPatternCoupling(SectionMeetingViewModel leader, List<SectionMeetingViewModel> followers)
    {
        _couplingLeader = leader;
        _couplingFollowers = followers;
        _couplingRemainingFields = new HashSet<string>
        {
            nameof(SectionMeetingViewModel.SelectedBlockLength),
            nameof(SectionMeetingViewModel.SelectedStartTime),
            nameof(SectionMeetingViewModel.SelectedMeetingTypeId),
        };

        _couplingHandler = (_, e) =>
        {
            if (e.PropertyName is null || !_couplingRemainingFields.Contains(e.PropertyName)) return;

            // Decouple this field and propagate its current value to all followers.
            _couplingRemainingFields.Remove(e.PropertyName);
            foreach (var follower in _couplingFollowers)
            {
                switch (e.PropertyName)
                {
                    case nameof(SectionMeetingViewModel.SelectedBlockLength):
                        follower.SelectedBlockLength = leader.SelectedBlockLength;
                        break;
                    case nameof(SectionMeetingViewModel.SelectedStartTime):
                        follower.SelectedStartTime = leader.SelectedStartTime;
                        break;
                    case nameof(SectionMeetingViewModel.SelectedMeetingTypeId):
                        follower.SelectedMeetingTypeId = leader.SelectedMeetingTypeId;
                        break;
                }
            }

            // Once all three fields have propagated, tear down the coupling entirely.
            if (_couplingRemainingFields.Count == 0)
                TearDownPatternCoupling();
        };

        leader.PropertyChanged += _couplingHandler;
    }

    /// <summary>
    /// Unsubscribes the coupling handler from the leader and resets all coupling state.
    /// Safe to call even when no coupling is active.
    /// </summary>
    private void TearDownPatternCoupling()
    {
        if (_couplingLeader != null && _couplingHandler != null)
            _couplingLeader.PropertyChanged -= _couplingHandler;

        _couplingLeader = null;
        _couplingFollowers = new();
        _couplingRemainingFields = new();
        _couplingHandler = null;
    }

    /// <summary>
    /// Merges the tags of the specified course into <see cref="TagSelections"/>.
    /// Any tag already checked is left unchanged; unchecked tags that belong to the course
    /// are checked on. Tags not on the course are never unchecked — this is a merge, not
    /// a replace.
    /// </summary>
    /// <param name="courseId">The course whose tags should be merged in.</param>
    private void MergeCourseTags(string courseId)
    {
        var course = Courses.FirstOrDefault(c => c.Id == courseId);
        if (course is null || course.TagIds.Count == 0) return;

        foreach (var tagSelection in TagSelections)
        {
            if (!tagSelection.IsSelected && course.TagIds.Contains(tagSelection.Value.Id))
                tagSelection.IsSelected = true;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        var trimmedCode = SectionCode.Trim();

        // Guard: must have course and section code
        if (string.IsNullOrEmpty(SelectedCourseId) || string.IsNullOrEmpty(trimmedCode))
            return;

        // Guard: section code must be unique (re-check in case user bypassed LostFocus)
        if (_isSectionCodeDuplicate(SelectedCourseId, trimmedCode))
        {
            SectionCodeError = "A section with this code already exists for this course in the selected semester.";
            return;
        }

        _section.CourseId = SelectedCourseId;
        _section.SectionCode = trimmedCode;

        // Copy the course's level band to the section so it can be filtered
        // without a course lookup (e.g. "100", "300", or empty when not set).
        _section.Level = Courses.FirstOrDefault(c => c.Id == SelectedCourseId)?.Level;
        _section.Notes = Notes.Trim();
        _section.Schedule = Meetings
            .Select(m => m.ToSchedule())
            .Where(s => s is not null)
            .Cast<SectionDaySchedule>()
            .ToList();

        // Multi-select instructors with workload
        _section.InstructorAssignments = InstructorSelections
            .Where(i => i.IsSelected)
            .Select(i => new InstructorAssignment { InstructorId = i.Value.Id, Workload = i.ParsedWorkload })
            .ToList();

        // Single-select properties (treat empty string sentinel as null)
        _section.SectionTypeId = string.IsNullOrEmpty(SelectedSectionTypeId) ? null : SelectedSectionTypeId;
        _section.CampusId      = string.IsNullOrEmpty(SelectedCampusId)      ? null : SelectedCampusId;

        // Multi-select properties
        _section.TagIds = TagSelections
            .Where(t => t.IsSelected)
            .Select(t => t.Value.Id)
            .ToList();

        _section.ResourceIds = ResourceSelections
            .Where(r => r.IsSelected)
            .Select(r => r.Value.Id)
            .ToList();

        _section.Reserves = ReserveSelections
            .Where(r => r.IsSelected && r.ParsedCode.HasValue)
            .Select(r => new SectionReserve { ReserveId = r.Value.Id, Code = r.ParsedCode!.Value })
            .ToList();

        await _onSave(_section);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
