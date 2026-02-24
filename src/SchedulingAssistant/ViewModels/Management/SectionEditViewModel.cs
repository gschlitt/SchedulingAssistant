using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionEditViewModel : ViewModelBase
{
    [ObservableProperty] private string? _selectedCourseId;
    [ObservableProperty] private string _sectionCode = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private ObservableCollection<SectionMeetingViewModel> _meetings = new();
    [ObservableProperty] private ObservableCollection<Course> _courses;

    // ── Step-gate state ──────────────────────────────────────────────────────

    /// <summary>True once a course is selected; enables the Section Code field.</summary>
    public bool IsSectionCodeEnabled => !string.IsNullOrEmpty(SelectedCourseId);

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
    [ObservableProperty] private ObservableCollection<SectionPropertyValue> _campuses = new();

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
                    return w.HasValue ? $"{i.DisplayName} [{w.Value:0.#}]" : i.DisplayName;
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
    private readonly Action<Section> _onSave;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly IReadOnlyList<SectionPropertyValue> _meetingTypes;
    private readonly IReadOnlyList<Room> _rooms;
    private readonly bool _includeSaturday;
    private readonly double? _defaultBlockLength;

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
        OnPropertyChanged(nameof(IsSectionCodeEnabled));
        OnPropertyChanged(nameof(AreOtherFieldsEnabled));
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
        }
        OnPropertyChanged(nameof(AreOtherFieldsEnabled));
    }

    public SectionEditViewModel(
        Section section,
        bool isNew,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Instructor> instructors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        IReadOnlyList<SectionPropertyValue> sectionTypes,
        IReadOnlyList<SectionPropertyValue> meetingTypes,
        IReadOnlyList<SectionPropertyValue> campuses,
        IReadOnlyList<SectionPropertyValue> allTags,
        IReadOnlyList<SectionPropertyValue> allResources,
        IReadOnlyList<SectionPropertyValue> allReserves,
        Func<string, string, bool> isSectionCodeDuplicate,
        Action<Section> onSave,
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

        Courses = new ObservableCollection<Course>(courses);

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
        Campuses     = BuildSentinelList(campuses);

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
        Meetings.Remove(meeting);
    }

    [RelayCommand]
    private void Save()
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

        _onSave(_section);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
