using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.GridView;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionListViewModel : ViewModelBase
{
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly SubjectRepository _subjectRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly LegalStartTimeRepository _legalStartTimeRepo;
    private readonly SemesterRepository _semesterRepo;
    private readonly BlockPatternRepository _blockPatternRepo;
    private readonly SectionPrefixRepository _prefixRepo;
    private readonly SemesterContext _semesterContext;
    private readonly ScheduleGridViewModel _scheduleGridVm;
    private readonly SectionPropertyRepository _propertyRepo;

    // ── Observable Properties ──────────────────────────────────────────────────

    /// <summary>
    /// The flat list of items shown in the Section List.
    /// Contains a mix of <see cref="SemesterBannerViewModel"/> (group headers)
    /// and <see cref="SectionListItemViewModel"/> (section cards).
    /// Banners appear only when more than one semester is loaded; in single-semester
    /// mode the list contains only section cards.
    /// </summary>
    [ObservableProperty] private ObservableCollection<ISectionListEntry> _sectionItems = new();

    /// <summary>
    /// The currently selected list entry.  May be a section card or (transiently) a banner.
    /// Use <see cref="SelectedSectionItem"/> for a cast-safe accessor that returns null for banners.
    /// </summary>
    [ObservableProperty] private ISectionListEntry? _selectedItem;

    [ObservableProperty] private SectionListItemViewModel? _expandedItem;
    [ObservableProperty] private SectionEditViewModel? _editVm;
    [ObservableProperty] private string? _lastErrorMessage;
    [ObservableProperty] private string _sortModeLabel = "";

    /// <summary>
    /// Whether the "Add to which semester?" inline prompt is visible.
    /// Set to true by <see cref="Add"/> in multi-semester mode; hidden after a selection
    /// or cancel.
    /// </summary>
    [ObservableProperty] private bool _isAddSemesterPromptVisible;

    // ── Non-Observable State ───────────────────────────────────────────────────

    private readonly IDialogService _dialog;
    private SectionSortMode _sortMode;

    /// <summary>
    /// Semester options shown in the Add prompt.  Rebuilt each time the prompt opens.
    /// </summary>
    public IReadOnlyList<SemesterPromptItem> AddSemesterOptions { get; private set; } = [];

    /// <summary>
    /// Index of the most recently selected <em>section</em> item.  Captured whenever a
    /// SectionListItemViewModel is selected so that "Add" in single-semester mode can
    /// insert after it even if the ListBox has cleared SelectedItem by the time the
    /// command runs.
    /// </summary>
    private int _lastSelectedIndex = -1;
    private bool _suppressSelectionSync = false;

    // ── Computed Properties ────────────────────────────────────────────────────

    /// <summary>True when an inline editor is open (Add or Edit mode).</summary>
    public bool IsEditing => EditVm is not null;

    /// <summary>
    /// The currently selected section item, or null if nothing is selected or a banner is selected.
    /// Safe to use without type-casting.
    /// </summary>
    public SectionListItemViewModel? SelectedSectionItem => SelectedItem as SectionListItemViewModel;

    /// <summary>The section model of the currently selected section, or null.</summary>
    public Section? SelectedSection => SelectedSectionItem?.Section;

    /// <summary>True when more than one semester is currently loaded.</summary>
    public bool IsMultiSemesterMode => _semesterContext.IsMultiSemesterMode;

    /// <summary>Expose SemesterContext for debug view access.</summary>
    public SemesterContext SemesterContext => _semesterContext;

    /// <summary>Current sort mode; used by context menu to show checkmarks.</summary>
    public SectionSortMode CurrentSortMode => _sortMode;

    // ── Property-Changed Hooks ─────────────────────────────────────────────────

    partial void OnEditVmChanged(SectionEditViewModel? value) =>
        OnPropertyChanged(nameof(IsEditing));

    /// <summary>
    /// Called when the list selection changes.
    /// Ignores banners (they are not real selections).
    /// Updates <see cref="_lastSelectedIndex"/> and syncs the selected section to the grid.
    /// </summary>
    partial void OnSelectedItemChanged(ISectionListEntry? value)
    {
        if (value is SemesterBannerViewModel) return; // banners are not selectable entities

        if (value is SectionListItemViewModel svm)
            _lastSelectedIndex = SectionItems.IndexOf(svm);

        // Push selection to the grid (guard against echo-back)
        if (!_suppressSelectionSync)
        {
            _suppressSelectionSync = true;
            _scheduleGridVm.SelectedSectionId = (value as SectionListItemViewModel)?.Section.Id;
            _suppressSelectionSync = false;
        }
    }

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>Fired when sections are loaded/reloaded (after successful Load()).</summary>
    public event Action? SectionsChanged;

    // ── Constructor ────────────────────────────────────────────────────────────

    public SectionListViewModel(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        SubjectRepository subjectRepo,
        InstructorRepository instructorRepo,
        RoomRepository roomRepo,
        LegalStartTimeRepository legalStartTimeRepo,
        SemesterRepository semesterRepo,
        BlockPatternRepository blockPatternRepo,
        SectionPrefixRepository prefixRepo,
        SemesterContext semesterContext,
        ScheduleGridViewModel scheduleGridVm,
        SectionPropertyRepository propertyRepo,
        SectionChangeNotifier changeNotifier,
        IDialogService dialog)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _subjectRepo = subjectRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _legalStartTimeRepo = legalStartTimeRepo;
        _semesterRepo = semesterRepo;
        _blockPatternRepo = blockPatternRepo;
        _prefixRepo = prefixRepo;
        _semesterContext = semesterContext;
        _scheduleGridVm = scheduleGridVm;
        _propertyRepo = propertyRepo;
        _dialog = dialog;

        _semesterContext.PropertyChanged += OnSemesterContextChanged;
        _scheduleGridVm.PropertyChanged += OnGridVmPropertyChanged;
        _scheduleGridVm.EditRequested = EditSectionById;
        changeNotifier.SectionChanged += Reload;

        _sortMode = AppSettings.Load().SectionSortMode;
        UpdateSortModeLabel();

        Load();
    }

    // ── Context Change Handlers ────────────────────────────────────────────────

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
        {
            // Fires whenever the selected semester set changes (add, remove, or replace).
            // IsMultiSemesterMode may have also changed; notify the view.
            OnPropertyChanged(nameof(IsMultiSemesterMode));
            Load();
        }
    }

    private void OnGridVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScheduleGridViewModel.SelectedSectionId)) return;
        if (_suppressSelectionSync) return;

        var id = _scheduleGridVm.SelectedSectionId;
        if (id == SelectedSectionItem?.Section.Id) return;

        _suppressSelectionSync = true;
        SelectedItem = id is null
            ? null
            : SectionItems.OfType<SectionListItemViewModel>().FirstOrDefault(i => i.Section.Id == id);
        _suppressSelectionSync = false;
    }

    // ── Load / Reload ──────────────────────────────────────────────────────────

    /// <summary>Reloads the section list from the database.</summary>
    public void Reload() => Load();

    private void Load(string? selectSectionId = null)
    {
        try
        {
            LoadCore(selectSectionId);
            LastErrorMessage = null;
            SectionsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SectionListViewModel.Load");
            SectionItems = new();
            LastErrorMessage = "An error occurred loading the section list. See logs for details.";
        }
    }

    /// <summary>
    /// Core load logic.  Iterates over all selected semesters and builds a flat list of
    /// <see cref="ISectionListEntry"/> items — a <see cref="SemesterBannerViewModel"/>
    /// header followed by sorted <see cref="SectionListItemViewModel"/>s for each semester.
    /// In single-semester mode no banners are inserted.
    /// </summary>
    /// <param name="selectSectionId">
    /// If non-null, the section with this ID is selected after the list is rebuilt.
    /// </param>
    private void LoadCore(string? selectSectionId)
    {
        _lastSelectedIndex = -1;

        var semesters = _semesterContext.SelectedSemesters.ToList();
        if (semesters.Count == 0) { SectionItems = new(); return; }

        App.Logger.LogInfo($"LoadCore: Loading sections for {semesters.Count} semester(s)", "LoadCore");

        bool showBanners = semesters.Count > 1;

        // Build shared lookup tables once (used across all semesters)
        var courseLookup      = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructorLookup  = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var roomLookup        = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var sectionTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.SectionType).ToDictionary(v => v.Id);
        var campusLookup      = _propertyRepo.GetAll(SectionPropertyTypes.Campus).ToDictionary(v => v.Id);
        var tagLookup         = _propertyRepo.GetAll(SectionPropertyTypes.Tag).ToDictionary(v => v.Id);
        var resourceLookup    = _propertyRepo.GetAll(SectionPropertyTypes.Resource).ToDictionary(v => v.Id);
        var reserveLookup     = _propertyRepo.GetAll(SectionPropertyTypes.Reserve).ToDictionary(v => v.Id);
        var meetingTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType).ToDictionary(v => v.Id);

        // Snapshot collapsed state across all existing section items so it survives the rebuild
        var collapsedIds = SectionItems
            .OfType<SectionListItemViewModel>()
            .Where(i => i.IsCollapsed)
            .Select(i => i.Section.Id)
            .ToHashSet();

        var newItems = new List<ISectionListEntry>();

        for (int i = 0; i < semesters.Count; i++)
        {
            var semDisplay = semesters[i];

            if (showBanners)
                newItems.Add(new SemesterBannerViewModel(semDisplay, i));

            var sections = _sectionRepo.GetAll(semDisplay.Semester.Id);
            App.Logger.LogInfo(
                $"LoadCore: {sections.Count} sections for semester {semDisplay.Semester.Id}", "LoadCore");

            var rawItems = sections.Select(s =>
            {
                var vm = new SectionListItemViewModel(
                    s, courseLookup, instructorLookup, roomLookup,
                    sectionTypeLookup, campusLookup,
                    tagLookup, resourceLookup, reserveLookup, meetingTypeLookup,
                    semDisplay.Semester.Name);
                if (collapsedIds.Contains(s.Id))
                    vm.IsCollapsed = true;
                return vm;
            });

            // Sort within this semester's group independently
            newItems.AddRange(SortItems(rawItems));
        }

        SectionItems = new ObservableCollection<ISectionListEntry>(newItems);

        App.Logger.LogInfo($"LoadCore: Built {SectionItems.Count} list entries total", "LoadCore");

        if (selectSectionId is not null)
            SelectedItem = SectionItems.OfType<SectionListItemViewModel>()
                .FirstOrDefault(i => i.Section.Id == selectSectionId);
    }

    // ── Sorting ────────────────────────────────────────────────────────────────

    private void UpdateSortModeLabel() =>
        SortModeLabel = _sortMode switch
        {
            SectionSortMode.Instructor  => "Instructor",
            SectionSortMode.SectionType => "Section Type",
            _                           => "Subject/Course",
        };

    /// <summary>
    /// Returns <paramref name="items"/> sorted according to the current sort mode.
    /// Secondary sort is always by heading (course+section code) for stability.
    /// </summary>
    private IEnumerable<SectionListItemViewModel> SortItems(IEnumerable<SectionListItemViewModel> items) =>
        _sortMode switch
        {
            SectionSortMode.Instructor  => items.OrderBy(i => i.SortKeyInstructor,  StringComparer.Ordinal)
                                                .ThenBy(i => i.Heading),
            SectionSortMode.SectionType => items.OrderBy(i => i.SortKeySectionType, StringComparer.Ordinal)
                                                .ThenBy(i => i.Heading),
            _                           => items.OrderBy(i => i.Heading),
        };

    /// <summary>
    /// Re-sorts the existing list in-place, preserving semester group boundaries.
    /// Each semester group is sorted independently; banners stay at the top of their group.
    /// Selection and expanded-editor state are preserved by ID.
    /// </summary>
    private void ApplySort()
    {
        var selectedId = SelectedSectionItem?.Section.Id;
        var expandedId = ExpandedItem?.Section.Id;

        _suppressSelectionSync = true;
        try
        {
            var sorted = new List<ISectionListEntry>();
            var currentGroup = new List<SectionListItemViewModel>();
            ISectionListEntry? pendingBanner = null;

            void FlushGroup()
            {
                if (pendingBanner != null)
                    sorted.Add(pendingBanner);
                sorted.AddRange(SortItems(currentGroup));
                currentGroup.Clear();
                pendingBanner = null;
            }

            foreach (var item in SectionItems)
            {
                if (item is SemesterBannerViewModel)
                {
                    FlushGroup();
                    pendingBanner = item;
                }
                else if (item is SectionListItemViewModel svm)
                {
                    currentGroup.Add(svm);
                }
            }
            FlushGroup(); // flush the last group (no banner follows it)

            SectionItems = new ObservableCollection<ISectionListEntry>(sorted);

            if (selectedId is not null)
                SelectedItem = SectionItems.OfType<SectionListItemViewModel>()
                    .FirstOrDefault(i => i.Section.Id == selectedId);

            if (expandedId is not null)
                ExpandedItem = SectionItems.OfType<SectionListItemViewModel>()
                    .FirstOrDefault(i => i.Section.Id == expandedId);
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    [RelayCommand]
    private void SortBySubjectCourseCode() => SetSortMode(SectionSortMode.SubjectCourseCode);

    [RelayCommand]
    private void SortByInstructor() => SetSortMode(SectionSortMode.Instructor);

    [RelayCommand]
    private void SortBySectionType() => SetSortMode(SectionSortMode.SectionType);

    private void SetSortMode(SectionSortMode mode)
    {
        _sortMode = mode;
        UpdateSortModeLabel();
        var s = AppSettings.Load(); s.SectionSortMode = mode; s.Save();
        ApplySort();
    }

    // ── Collapse / Expand All ──────────────────────────────────────────────────

    [RelayCommand]
    public void CollapseAll()
    {
        foreach (var item in SectionItems.OfType<SectionListItemViewModel>())
            item.IsCollapsed = true;
    }

    [RelayCommand]
    public void ExpandAll()
    {
        foreach (var item in SectionItems.OfType<SectionListItemViewModel>())
            item.IsCollapsed = false;
    }

    // ── Add ────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Add()
    {
        if (_semesterContext.IsMultiSemesterMode)
        {
            // If a section is selected, default to its semester without prompting.
            // If nothing is selected, ask the user which semester to add to.
            if (SelectedSection is not null)
                AddToSemester(SelectedSection.SemesterId);
            else
                ShowAddSemesterPrompt();
            return;
        }

        var semester = _semesterContext.SelectedSemesterDisplay?.Semester;
        if (semester is null) return;

        // In single-semester mode, insert after the last-selected item (or at end)
        int insertAt = _lastSelectedIndex >= 0 && _lastSelectedIndex < SectionItems.Count
            ? _lastSelectedIndex + 1
            : SectionItems.Count;

        var section = new Section { SemesterId = semester.Id };
        OpenAdd(section, insertAt);
    }

    /// <summary>
    /// Builds the <see cref="AddSemesterOptions"/> list from the currently selected semesters
    /// and makes the prompt panel visible.
    /// </summary>
    private void ShowAddSemesterPrompt()
    {
        var semesters = _semesterContext.SelectedSemesters.ToList();
        AddSemesterOptions = semesters
            .Select((s, i) => new SemesterPromptItem(s, i))
            .ToList();
        OnPropertyChanged(nameof(AddSemesterOptions));
        IsAddSemesterPromptVisible = true;
    }

    /// <summary>
    /// Called when the user picks a semester from the Add prompt.
    /// Hides the prompt, finds the right insertion point in that semester's group,
    /// and opens the inline editor.
    /// </summary>
    /// <param name="semesterId">The ID of the semester to add to.</param>
    [RelayCommand]
    private void AddToSemester(string semesterId)
    {
        IsAddSemesterPromptVisible = false;

        var section = new Section { SemesterId = semesterId };
        int insertAt = FindInsertionIndex(semesterId);
        OpenAdd(section, insertAt);
    }

    /// <summary>Hides the Add prompt without adding a section.</summary>
    [RelayCommand]
    private void CancelAddPrompt() => IsAddSemesterPromptVisible = false;

    /// <summary>
    /// Finds the best index at which to insert a new section for the given semester.
    /// Returns the index immediately after the last existing section in that semester's
    /// group, or immediately after the group's banner if the group is empty.
    /// Falls back to end-of-list if neither is found.
    /// </summary>
    /// <param name="semesterId">Target semester ID.</param>
    private int FindInsertionIndex(string semesterId)
    {
        // Walk backward to find the last section belonging to this semester
        for (int i = SectionItems.Count - 1; i >= 0; i--)
        {
            if (SectionItems[i] is SectionListItemViewModel vm && vm.Section.SemesterId == semesterId)
                return i + 1;
        }

        // No sections yet — insert right after the semester's banner
        for (int i = 0; i < SectionItems.Count; i++)
        {
            if (SectionItems[i] is SemesterBannerViewModel banner && banner.SemesterId == semesterId)
                return i + 1;
        }

        // Fallback: after last selected item or at end
        return _lastSelectedIndex >= 0 && _lastSelectedIndex < SectionItems.Count
            ? _lastSelectedIndex + 1
            : SectionItems.Count;
    }

    /// <summary>
    /// Opens the inline Add editor for a new section at the given list index.
    /// The section's SemesterId is already set; the academic year is derived from
    /// <see cref="SemesterContext.SelectedAcademicYear"/> (all selected semesters share one AY).
    /// </summary>
    private void OpenAdd(Section section, int insertIndex)
    {
        if (ExpandedItem is not null) ExpandedItem.IsExpanded = false;

        // All selected semesters share the same academic year
        var ayId = _semesterContext.SelectedAcademicYear?.Id;
        if (string.IsNullOrEmpty(ayId)) return;

        var courses         = _courseRepo.GetAllActive();
        var instructors     = _instructorRepo.GetAll();
        var rooms           = _roomRepo.GetAll();
        var legalStartTimes = _legalStartTimeRepo.GetAll(ayId);
        var settings        = AppSettings.Load();
        var includeSaturday = settings.IncludeSaturday;

        var sectionTypes  = _propertyRepo.GetAll(SectionPropertyTypes.SectionType);
        var meetingTypes  = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType);
        var campuses      = _propertyRepo.GetAll(SectionPropertyTypes.Campus);
        var allTags       = _propertyRepo.GetAll(SectionPropertyTypes.Tag);
        var allResources  = _propertyRepo.GetAll(SectionPropertyTypes.Resource);
        var allReserves   = _propertyRepo.GetAll(SectionPropertyTypes.Reserve);

        // Build lookups for the placeholder's display row
        var courseLookup      = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructorLookup  = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var roomLookup        = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var sectionTypeLookup = sectionTypes.ToDictionary(v => v.Id);
        var campusLookup      = campuses.ToDictionary(v => v.Id);
        var tagLookup         = _propertyRepo.GetAll(SectionPropertyTypes.Tag).ToDictionary(v => v.Id);
        var resourceLookup    = _propertyRepo.GetAll(SectionPropertyTypes.Resource).ToDictionary(v => v.Id);
        var reserveLookup     = _propertyRepo.GetAll(SectionPropertyTypes.Reserve).ToDictionary(v => v.Id);
        var meetingTypeLookup = meetingTypes.ToDictionary(v => v.Id);

        var placeholder = new SectionListItemViewModel(
            section, courseLookup, instructorLookup, roomLookup,
            sectionTypeLookup, campusLookup, tagLookup, resourceLookup, reserveLookup, meetingTypeLookup);
        placeholder.IsBeingCreated = true;

        if (insertIndex > SectionItems.Count) insertIndex = SectionItems.Count;
        SectionItems.Insert(insertIndex, placeholder);

        // Uniqueness is enforced within the section's own semester
        var semesterId = section.SemesterId;
        var subjects = _subjectRepo.GetAll();
        var editVm = new SectionEditViewModel(
            section, isNew: true,
            courses, subjects, instructors, rooms, legalStartTimes, includeSaturday,
            sectionTypes, meetingTypes, campuses,
            allTags, allResources, allReserves,
            isSectionCodeDuplicate: (courseId, code) =>
                _sectionRepo.ExistsBySectionCode(semesterId, courseId, code, excludeId: null),
            onSave: async s =>
            {
                try
                {
                    _sectionRepo.Insert(s);
                    CollapseEditor();
                    Load(selectSectionId: s.Id);
                    _scheduleGridVm.Reload();
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, "SectionListViewModel.Add");
                    await _dialog.ShowError("The save could not be completed. Please try again.");
                }
            },
            _blockPatternRepo,
            _prefixRepo,
            defaultBlockLength: settings.PreferredBlockLength);

        editVm.RequestClose = () =>
        {
            SectionItems.Remove(placeholder);
            CollapseEditor();
        };

        ExpandedItem = placeholder;
        placeholder.IsExpanded = true;
        EditVm = editVm;
    }

    // ── Edit ───────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Edit()
    {
        if (SelectedSectionItem is null) return;
        OpenEdit(CloneSection(SelectedSectionItem.Section), isNew: false, listItem: SelectedSectionItem);
    }

    /// <summary>
    /// Relay command wired by LostFocusForwardBehavior in the AXAML. When the Section Code
    /// TextBox (which lives inside a DataTemplate) loses focus, the behavior invokes this
    /// command, which delegates to the active editor's CommitSectionCode(). This validates
    /// the section code for uniqueness and records the validated snapshot that unlocks the
    /// rest of the form (see SectionEditViewModel and the step-gate pattern in CLAUDE.md).
    /// </summary>
    [RelayCommand]
    private void CommitEditSectionCode() => EditVm?.CommitSectionCode();

    /// <summary>Called from the view when a list item is double-tapped.</summary>
    public void EditItem(SectionListItemViewModel item)
    {
        SelectedItem = item;

        // Guard: if this item is already open, do nothing.
        // Without this, double-tapping an already-open section's header recreates the
        // SectionEditViewModel, causing ComboBox binding races that blank out selections.
        if (ExpandedItem == item) return;

        OpenEdit(CloneSection(item.Section), isNew: false, listItem: item);
    }

    /// <summary>
    /// Called from the grid when a tile is double-clicked.
    /// Selects the item in the list and opens its inline editor.
    /// </summary>
    public void EditSectionById(string sectionId)
    {
        var item = SectionItems.OfType<SectionListItemViewModel>()
                               .FirstOrDefault(i => i.Section.Id == sectionId);
        if (item is null) return;
        EditItem(item);
    }

    private void OpenEdit(Section section, bool isNew, SectionListItemViewModel? listItem)
    {
        if (ExpandedItem is not null) ExpandedItem.IsExpanded = false;

        // All selected semesters share the same academic year
        var ayId = _semesterContext.SelectedAcademicYear?.Id;
        if (string.IsNullOrEmpty(ayId)) return;

        var courses         = _courseRepo.GetAllActive();
        var instructors     = _instructorRepo.GetAll();
        var rooms           = _roomRepo.GetAll();
        var legalStartTimes = _legalStartTimeRepo.GetAll(ayId);
        var settings        = AppSettings.Load();
        var includeSaturday = settings.IncludeSaturday;

        var sectionTypes  = _propertyRepo.GetAll(SectionPropertyTypes.SectionType);
        var meetingTypes  = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType);
        var campuses      = _propertyRepo.GetAll(SectionPropertyTypes.Campus);
        var allTags       = _propertyRepo.GetAll(SectionPropertyTypes.Tag);
        var allResources  = _propertyRepo.GetAll(SectionPropertyTypes.Resource);
        var allReserves   = _propertyRepo.GetAll(SectionPropertyTypes.Reserve);

        // Uniqueness is scoped to the section's own semester
        var semesterId = section.SemesterId;
        var subjects = _subjectRepo.GetAll();
        var editVm = new SectionEditViewModel(
            section, isNew,
            courses, subjects, instructors, rooms, legalStartTimes, includeSaturday,
            sectionTypes, meetingTypes, campuses,
            allTags, allResources, allReserves,
            (courseId, code) =>
                _sectionRepo.ExistsBySectionCode(semesterId, courseId, code, isNew ? null : section.Id),
            onSave: async s =>
            {
                try
                {
                    if (isNew) _sectionRepo.Insert(s); else _sectionRepo.Update(s);
                    CollapseEditor();
                    Load(selectSectionId: s.Id);
                    _scheduleGridVm.Reload();
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, "SectionListViewModel.Edit");
                    await _dialog.ShowError("The save could not be completed. Please try again.");
                }
            },
            _blockPatternRepo,
            _prefixRepo,
            defaultBlockLength: settings.PreferredBlockLength);

        editVm.RequestClose = CollapseEditor;
        ExpandedItem = listItem;
        if (listItem is not null) listItem.IsExpanded = true;
        EditVm = editVm;
    }

    // ── Copy ───────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Copy()
    {
        if (SelectedSectionItem is null) return;
        var source = SelectedSectionItem.Section;

        // Copy always stays in the same semester as the source section.
        var semesterId = source.SemesterId;
        if (string.IsNullOrEmpty(semesterId)) return;

        // Derive the next available section code using the shared prefix helper.
        // - If the source code starts with a known prefix, gap-fill that prefix's sequence.
        // - Otherwise, increment the trailing integer by one (simple advance fallback).
        // Campus is taken from the matched prefix's association (or null if no prefix matched).
        string? newCode    = null;
        string? newCampusId = null;

        if (!string.IsNullOrEmpty(source.CourseId))
        {
            var prefixes = _prefixRepo.GetAll();
            Func<string, bool> codeExists =
                code => _sectionRepo.ExistsBySectionCode(semesterId, source.CourseId, code, excludeId: null);

            (newCode, newCampusId) = SectionPrefixHelper.AdvanceSectionCode(
                source.SectionCode, prefixes, codeExists);

            if (newCode is null)
                await _dialog.ShowError(
                    "Could not auto-assign a section code: the next candidate is already taken. " +
                    "The section code has been left blank — please enter one before saving.");
        }

        var newSection = new Section
        {
            SemesterId  = semesterId,
            CourseId    = source.CourseId,
            SectionCode = newCode ?? string.Empty,
            CampusId    = newCampusId,
        };

        OpenCopy(newSection, afterItem: SelectedSectionItem);
    }

    private void OpenCopy(Section section, SectionListItemViewModel afterItem)
    {
        if (ExpandedItem is not null) ExpandedItem.IsExpanded = false;

        // All selected semesters share the same academic year
        var ayId = _semesterContext.SelectedAcademicYear?.Id;
        if (string.IsNullOrEmpty(ayId)) return;

        var courses         = _courseRepo.GetAllActive();
        var instructors     = _instructorRepo.GetAll();
        var rooms           = _roomRepo.GetAll();
        var legalStartTimes = _legalStartTimeRepo.GetAll(ayId);
        var settings        = AppSettings.Load();
        var includeSaturday = settings.IncludeSaturday;

        var sectionTypes  = _propertyRepo.GetAll(SectionPropertyTypes.SectionType);
        var meetingTypes  = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType);
        var campuses      = _propertyRepo.GetAll(SectionPropertyTypes.Campus);
        var allTags       = _propertyRepo.GetAll(SectionPropertyTypes.Tag);
        var allResources  = _propertyRepo.GetAll(SectionPropertyTypes.Resource);
        var allReserves   = _propertyRepo.GetAll(SectionPropertyTypes.Reserve);

        var courseLookup      = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var instructorLookup  = _instructorRepo.GetAll().ToDictionary(i => i.Id);
        var roomLookup        = _roomRepo.GetAll().ToDictionary(r => r.Id);
        var sectionTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.SectionType).ToDictionary(v => v.Id);
        var campusLookup      = _propertyRepo.GetAll(SectionPropertyTypes.Campus).ToDictionary(v => v.Id);
        var tagLookup         = _propertyRepo.GetAll(SectionPropertyTypes.Tag).ToDictionary(v => v.Id);
        var resourceLookup    = _propertyRepo.GetAll(SectionPropertyTypes.Resource).ToDictionary(v => v.Id);
        var reserveLookup     = _propertyRepo.GetAll(SectionPropertyTypes.Reserve).ToDictionary(v => v.Id);
        var meetingTypeLookup = _propertyRepo.GetAll(SectionPropertyTypes.MeetingType).ToDictionary(v => v.Id);

        var placeholder = new SectionListItemViewModel(
            section, courseLookup, instructorLookup, roomLookup,
            sectionTypeLookup, campusLookup, tagLookup, resourceLookup, reserveLookup, meetingTypeLookup);
        placeholder.IsBeingCreated = true;

        // Insert immediately after the source item
        int insertIndex = SectionItems.IndexOf(afterItem) + 1;
        SectionItems.Insert(insertIndex, placeholder);

        // Uniqueness scoped to the copy's semester (same as source)
        var semesterId = section.SemesterId;
        var subjects = _subjectRepo.GetAll();
        var editVm = new SectionEditViewModel(
            section, isNew: true,
            courses, subjects, instructors, rooms, legalStartTimes, includeSaturday,
            sectionTypes, meetingTypes, campuses,
            allTags, allResources, allReserves,
            isSectionCodeDuplicate: (courseId, code) =>
                _sectionRepo.ExistsBySectionCode(semesterId, courseId, code, excludeId: null),
            onSave: async s =>
            {
                try
                {
                    _sectionRepo.Insert(s);
                    CollapseEditor();
                    Load(selectSectionId: s.Id);
                    _scheduleGridVm.Reload();
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, "SectionListViewModel.Copy");
                    await _dialog.ShowError("The save could not be completed. Please try again.");
                }
            },
            _blockPatternRepo,
            _prefixRepo,
            defaultBlockLength: settings.PreferredBlockLength);

        editVm.RequestClose = () =>
        {
            SectionItems.Remove(placeholder);
            CollapseEditor();
        };

        ExpandedItem = placeholder;
        placeholder.IsExpanded = true;
        EditVm = editVm;
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedSection is null) return;
        if (ExpandedItem?.Section.Id == SelectedSection.Id)
            CollapseEditor();
        try
        {
            _sectionRepo.Delete(SelectedSection.Id);
            Load();
            _scheduleGridVm.Reload();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SectionListViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }

    // ── Error Banner ───────────────────────────────────────────────────────────

    [RelayCommand]
    public void DismissError() => LastErrorMessage = null;

    // ── Editor Collapse ────────────────────────────────────────────────────────

    private void CollapseEditor()
    {
        if (ExpandedItem is not null) ExpandedItem.IsExpanded = false;
        ExpandedItem = null;
        EditVm = null;
    }

    // ── Debug / Dev ────────────────────────────────────────────────────────────

#if DEBUG
    /// <summary>
    /// DEV ONLY — forces a load failure so the error banner can be tested visually.
    /// Remove before shipping.
    /// </summary>
    [RelayCommand]
    public void SimulateLoadError()
    {
        App.Logger.LogError(new InvalidOperationException("Simulated section list load error"), "SimulateLoadError");
        SectionItems = new();
        LastErrorMessage = "An error occurred loading the section list. See logs for details.";
    }

    /// <summary>
    /// DEV ONLY — Generate and insert random sections for testing.
    /// Remove before shipping.
    /// </summary>
    [RelayCommand]
    public void GenerateRandomSections(int count)
    {
        try
        {
            var semesterId = _semesterContext.SelectedSemesterDisplay?.Semester.Id;
            if (semesterId is null)
            {
                App.Logger.LogWarning("No semester selected for test data generation", "GenerateRandomSections");
                return;
            }

            App.Logger.LogInfo($"Starting generation of {count} sections for semester {semesterId}", "GenerateRandomSections");

            var generator = new DebugTestDataGenerator(
                _sectionRepo, _courseRepo, _instructorRepo, _roomRepo,
                _legalStartTimeRepo, _semesterRepo, _blockPatternRepo, _propertyRepo);

            var sections = generator.GenerateSections(count, semesterId);
            App.Logger.LogInfo($"Generator created {sections.Count} sections", "GenerateRandomSections");

            foreach (var section in sections)
            {
                App.Logger.LogInfo($"Inserting section {section.Id} ({section.SectionCode}) for course {section.CourseId}", "GenerateRandomSections");
                _sectionRepo.Insert(section);
            }

            App.Logger.LogInfo("All sections inserted, calling Load()...", "GenerateRandomSections");
            Load();

            App.Logger.LogInfo("Load() complete, calling ScheduleGridVm.Reload()...", "GenerateRandomSections");
            _scheduleGridVm.Reload();

            App.Logger.LogInfo($"Generated {sections.Count} test sections successfully", "GenerateRandomSections");
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "GenerateRandomSections");
        }
    }
#endif

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Section CloneSection(Section s) => new()
    {
        Id              = s.Id,
        SemesterId      = s.SemesterId,
        CourseId        = s.CourseId,
        InstructorAssignments = s.InstructorAssignments
            .Select(a => new InstructorAssignment { InstructorId = a.InstructorId, Workload = a.Workload })
            .ToList(),
        SectionCode     = s.SectionCode,
        Notes           = s.Notes,
        Schedule        = s.Schedule.Select(d => new SectionDaySchedule
        {
            Day             = d.Day,
            StartMinutes    = d.StartMinutes,
            DurationMinutes = d.DurationMinutes,
            MeetingTypeId   = d.MeetingTypeId,
            RoomId          = d.RoomId,
            Frequency       = d.Frequency,
        }).ToList(),
        SectionTypeId   = s.SectionTypeId,
        CampusId        = s.CampusId,
        TagIds          = new List<string>(s.TagIds),
        ResourceIds     = new List<string>(s.ResourceIds),
        Reserves        = s.Reserves.Select(r => new SectionReserve
            { ReserveId = r.ReserveId, Code = r.Code }).ToList(),
    };
}
