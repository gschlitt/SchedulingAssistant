using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionListViewModel : ViewModelBase
{
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ILegalStartTimeRepository _legalStartTimeRepo;
    private readonly ISemesterRepository _semesterRepo;
    private readonly IBlockPatternRepository _blockPatternRepo;
    private readonly ISectionPrefixRepository _prefixRepo;
    private readonly SemesterContext _semesterContext;
    private readonly SectionStore _sectionStore;
    private readonly ISectionPropertyRepository _propertyRepo;
    private readonly ICampusRepository _campusRepo;
    private readonly WriteLockService _lockService;

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

    /// <summary>True when the current user holds the write lock; controls whether CRUD commands are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>CanExecute predicate shared by all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    // ── Property-Changed Hooks ─────────────────────────────────────────────────

    partial void OnEditVmChanged(SectionEditViewModel? value) =>
        OnPropertyChanged(nameof(IsEditing));

    /// <summary>
    /// Called when the list selection changes.
    /// Ignores banners (they are not real selections).
    /// Updates <see cref="_lastSelectedIndex"/> and pushes the selection to the
    /// <see cref="SectionStore"/> so all other views stay in sync.
    /// <see cref="SectionStore.SetSelection"/> is idempotent — no suppress flag needed.
    /// </summary>
    partial void OnSelectedItemChanged(ISectionListEntry? value)
    {
        if (value is SemesterBannerViewModel) return; // banners are not selectable entities

        if (value is SectionListItemViewModel svm)
            _lastSelectedIndex = SectionItems.IndexOf(svm);

        _sectionStore.SetSelection((value as SectionListItemViewModel)?.Section.Id);
    }

    // ── Constructor ────────────────────────────────────────────────────────────

    public SectionListViewModel(
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        ISubjectRepository subjectRepo,
        IInstructorRepository instructorRepo,
        IRoomRepository roomRepo,
        ILegalStartTimeRepository legalStartTimeRepo,
        ISemesterRepository semesterRepo,
        IBlockPatternRepository blockPatternRepo,
        ISectionPrefixRepository prefixRepo,
        SemesterContext semesterContext,
        SectionStore sectionStore,
        ISectionPropertyRepository propertyRepo,
        ICampusRepository campusRepo,
        IDialogService dialog,
        WriteLockService lockService)
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
        _sectionStore = sectionStore;
        _propertyRepo = propertyRepo;
        _campusRepo = campusRepo;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;

        _semesterContext.PropertyChanged += OnSemesterContextChanged;

        // Reload the list whenever any external code updates the section cache
        // (e.g. the schedule grid context menu, commitment CRUD, or semester changes).
        _sectionStore.SectionsChanged += Reload;

        // Sync SelectedItem whenever the selection is changed from another view.
        _sectionStore.SelectionChanged += OnStoreSelectionChanged;

        // Highlight section cards that match the active Schedule Grid filter.
        _sectionStore.FilteredIdsChanged += ApplyFilterHighlights;

        _sortMode = AppSettings.Current.SectionSortMode;
        UpdateSortModeLabel();

        Load();
    }

    // ── Lock State ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the write lock state changes. Notifies all write commands so
    /// they re-evaluate their CanExecute, and updates the IsWriteEnabled binding.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        AddToSemesterCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    // ── Context Change Handlers ────────────────────────────────────────────────

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
        {
            // Fires whenever the selected semester set changes (add, remove, or replace).
            // IsMultiSemesterMode may have also changed; notify the view.
            OnPropertyChanged(nameof(IsMultiSemesterMode));

            // Refresh the shared section cache for the new semester set.
            // SectionsChanged fires after this, triggering Reload() on this VM,
            // ScheduleGridViewModel, and WorkloadPanelViewModel.
            var semesterIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
            _sectionStore.Reload(_sectionRepo, semesterIds);
        }
    }

    /// <summary>
    /// Called when the <see cref="SectionStore"/> selection changes from an external source
    /// (e.g. a tile click in the Schedule Grid or a chip click in the Workload Panel).
    /// Updates <see cref="SelectedItem"/> to match the new selection.
    /// <see cref="OnSelectedItemChanged"/> will then call <see cref="SectionStore.SetSelection"/>
    /// again, but the store's idempotency guard prevents a second event from firing.
    /// </summary>
    private void OnStoreSelectionChanged(string? sectionId)
    {
        if (SelectedSectionItem?.Section.Id == sectionId) return;
        SelectedItem = sectionId is null
            ? null
            : SectionItems.OfType<SectionListItemViewModel>().FirstOrDefault(i => i.Section.Id == sectionId);
    }

    // ── Load / Reload ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the section list from the existing <see cref="SectionStore"/> in-memory
    /// cache.  Use this after the store has already been refreshed (e.g. after a save or
    /// delete that called <c>_sectionStore.Reload</c> internally).
    /// <para>
    /// Do <em>not</em> use this to pick up changes committed by an external writer process —
    /// use <see cref="ReloadFromDatabase"/> instead, which re-queries the DB first.
    /// </para>
    /// </summary>
    public void Reload() => Load();

    /// <summary>
    /// Re-queries the database for the current semester set, updates the
    /// <see cref="SectionStore"/> cache, then rebuilds the section list.
    /// The <c>SectionsChanged</c> event cascades the refresh automatically to the
    /// Schedule Grid and Workload Panel via their existing subscriptions.
    /// <para>
    /// This is the correct entry point for the Refresh button in the read-only banner,
    /// where an external writer process may have committed changes since the cache was
    /// last populated.
    /// </para>
    /// </summary>
    public void ReloadFromDatabase()
    {
        var semesterIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
        _sectionStore.Reload(_sectionRepo, semesterIds);
        // Load() is triggered automatically via the _sectionStore.SectionsChanged subscription.
    }

    private void Load(string? selectSectionId = null)
    {
        try
        {
            LoadCore(selectSectionId);
            LastErrorMessage = null;
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
        var campusLookup      = _campusRepo.GetAll().ToDictionary(c => c.Id);
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

            // Read from the shared cache — no DB query here.
            var sections = _sectionStore.SectionsBySemester.TryGetValue(semDisplay.Semester.Id, out var cached)
                ? cached
                : Array.Empty<Section>();
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

        // Apply any active grid filter highlights to the freshly built items.
        ApplyFilterHighlights();

        if (selectSectionId is not null)
            SelectedItem = SectionItems.OfType<SectionListItemViewModel>()
                .FirstOrDefault(i => i.Section.Id == selectSectionId);
    }

    /// <summary>
    /// Updates <see cref="SectionListItemViewModel.IsFilterHighlighted"/> on every section card
    /// based on the current <see cref="SectionStore.FilteredSectionIds"/> set.
    /// When the store holds <c>null</c> (no regular filter active) all highlights are cleared.
    /// Called whenever the Schedule Grid filter changes and after the list is rebuilt.
    /// </summary>
    private void ApplyFilterHighlights()
    {
        var ids = _sectionStore.FilteredSectionIds;
        foreach (var item in SectionItems.OfType<SectionListItemViewModel>())
            item.IsFilterHighlighted = ids is not null && ids.Contains(item.Section.Id);
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
    /// <summary>
    /// Re-sorts the existing list in-place, preserving semester group boundaries.
    /// Each semester group is sorted independently; banners stay at the top of their group.
    /// Selection and expanded-editor state are preserved by ID.
    /// No suppress flag is needed: <see cref="SectionStore.SetSelection"/> is idempotent
    /// and will not fire a second event when the same section is re-selected after sorting.
    /// </summary>
    private void ApplySort()
    {
        var selectedId = SelectedSectionItem?.Section.Id;
        var expandedId = ExpandedItem?.Section.Id;

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
        var s = AppSettings.Current; s.SectionSortMode = mode; s.Save();
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
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
    [RelayCommand(CanExecute = nameof(CanWrite))]
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

    // ── Editor Context / Factory Helpers ──────────────────────────────────────

    /// <summary>
    /// All data required to open the inline section editor, loaded in one pass.
    /// Shared by <see cref="OpenAdd"/>, <see cref="OpenEdit"/>, and <see cref="OpenCopy"/>
    /// so that each method performs zero redundant DB queries.
    /// </summary>
    /// <param name="Courses">Active courses only — drives the editor's Course picker.</param>
    /// <param name="AllCourses">All courses including inactive — used for placeholder display lookups
    /// so an existing section whose course was later deactivated still renders correctly.</param>
    private sealed record EditorContext(
        List<Course>               Courses,
        List<Course>               AllCourses,
        List<Subject>              Subjects,
        List<Instructor>           Instructors,
        List<Room>                 Rooms,
        List<LegalStartTime>       LegalStartTimes,
        bool                       IncludeSaturday,
        double?                    DefaultBlockLength,
        List<SectionPropertyValue> SectionTypes,
        List<SectionPropertyValue> MeetingTypes,
        List<Campus> Campuses,
        List<SectionPropertyValue> AllTags,
        List<SectionPropertyValue> AllResources,
        List<SectionPropertyValue> AllReserves);

    /// <summary>
    /// Loads all data needed to open the inline editor and returns it as an
    /// <see cref="EditorContext"/>. Returns <c>null</c> when no academic year is selected,
    /// which callers treat as a signal to abort silently.
    /// </summary>
    private EditorContext? BuildEditorContext()
    {
        var ayId = _semesterContext.SelectedAcademicYear?.Id;
        if (string.IsNullOrEmpty(ayId)) return null;

        var settings = AppSettings.Current;
        return new EditorContext(
            Courses:            _courseRepo.GetAllActive(),
            AllCourses:         _courseRepo.GetAll(),
            Subjects:           _subjectRepo.GetAll(),
            Instructors:        _instructorRepo.GetAll(),
            Rooms:              _roomRepo.GetAll(),
            LegalStartTimes:    _legalStartTimeRepo.GetAll(ayId),
            IncludeSaturday:    settings.IncludeSaturday,
            DefaultBlockLength: settings.PreferredBlockLength,
            SectionTypes:       _propertyRepo.GetAll(SectionPropertyTypes.SectionType),
            MeetingTypes:       _propertyRepo.GetAll(SectionPropertyTypes.MeetingType),
            Campuses:           _campusRepo.GetAll(),
            AllTags:            _propertyRepo.GetAll(SectionPropertyTypes.Tag),
            AllResources:       _propertyRepo.GetAll(SectionPropertyTypes.Resource),
            AllReserves:        _propertyRepo.GetAll(SectionPropertyTypes.Reserve));
    }

    /// <summary>
    /// Creates the placeholder <see cref="SectionListItemViewModel"/> shown in the list while
    /// a new section is being created (Add or Copy flow). Lookup dictionaries are derived from
    /// the already-loaded <paramref name="ctx"/> lists — no additional DB queries are made.
    /// The returned item has <c>IsBeingCreated = true</c>.
    /// </summary>
    /// <param name="section">The new section model that the placeholder represents.</param>
    /// <param name="ctx">Editor context containing all needed list data.</param>
    private static SectionListItemViewModel BuildPlaceholder(Section section, EditorContext ctx)
    {
        var placeholder = new SectionListItemViewModel(
            section,
            ctx.AllCourses.ToDictionary(c => c.Id),
            ctx.Instructors.ToDictionary(i => i.Id),
            ctx.Rooms.ToDictionary(r => r.Id),
            ctx.SectionTypes.ToDictionary(v => v.Id),
            ctx.Campuses.ToDictionary(c => c.Id),
            ctx.AllTags.ToDictionary(v => v.Id),
            ctx.AllResources.ToDictionary(v => v.Id),
            ctx.AllReserves.ToDictionary(v => v.Id),
            ctx.MeetingTypes.ToDictionary(v => v.Id));
        placeholder.IsBeingCreated = true;
        return placeholder;
    }

    /// <summary>
    /// Constructs a fully configured <see cref="SectionEditViewModel"/> from the shared
    /// <paramref name="ctx"/>. Encapsulates:
    /// <list type="bullet">
    ///   <item>The duplicate-check lambda with the correct <c>excludeId</c>
    ///         (<c>null</c> for inserts, <c>section.Id</c> for updates).</item>
    ///   <item>The save action (insert or update) with error handling and store/list reload.</item>
    /// </list>
    /// </summary>
    /// <param name="section">The section being created or edited.</param>
    /// <param name="isNew">
    ///   <c>true</c> for Add/Copy (calls <c>Insert</c>); <c>false</c> for Edit (calls <c>Update</c>).
    /// </param>
    /// <param name="ctx">Editor context loaded by <see cref="BuildEditorContext"/>.</param>
    /// <param name="semesterId">Semester scoping section-code uniqueness enforcement.</param>
    /// <param name="callerTag">Short label used in error log entries (e.g. "Add", "Edit", "Copy").</param>
    /// <returns>A <see cref="SectionEditViewModel"/> ready to be assigned to <see cref="EditVm"/>.</returns>
    private SectionEditViewModel CreateEditVm(
        Section section, bool isNew, EditorContext ctx,
        string semesterId, string callerTag)
    {
        return new SectionEditViewModel(
            section, isNew,
            ctx.Courses, ctx.Subjects, ctx.Instructors, ctx.Rooms,
            ctx.LegalStartTimes, ctx.IncludeSaturday,
            ctx.SectionTypes, ctx.MeetingTypes, ctx.Campuses,
            ctx.AllTags, ctx.AllResources, ctx.AllReserves,
            isSectionCodeDuplicate: (courseId, code) =>
                _sectionRepo.ExistsBySectionCode(
                    semesterId, courseId, code,
                    excludeId: isNew ? null : section.Id),
            onSave: async s =>
            {
                try
                {
                    if (isNew) _sectionRepo.Insert(s); else _sectionRepo.Update(s);
                    CollapseEditor();
                    var semIds = _semesterContext.SelectedSemesters.Select(sem => sem.Semester.Id);
                    _sectionStore.Reload(_sectionRepo, semIds); // notifies Grid + Workload via SectionsChanged
                    Load(selectSectionId: s.Id);                // rebuild list with correct post-save selection
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, $"SectionListViewModel.{callerTag}");
                    await _dialog.ShowError("The save could not be completed. Please try again.");
                }
            },
            _blockPatternRepo,
            _prefixRepo,
            defaultBlockLength: ctx.DefaultBlockLength);
    }

    /// <summary>
    /// Opens the inline Add editor for a new section at the given list index.
    /// The section's SemesterId is already set; the academic year is derived from
    /// <see cref="SemesterContext.SelectedAcademicYear"/> (all selected semesters share one AY).
    /// </summary>
    private void OpenAdd(Section section, int insertIndex)
    {
        if (ExpandedItem is not null) ExpandedItem.IsExpanded = false;

        var ctx = BuildEditorContext();
        if (ctx is null) return;

        var placeholder = BuildPlaceholder(section, ctx);
        if (insertIndex > SectionItems.Count) insertIndex = SectionItems.Count;
        SectionItems.Insert(insertIndex, placeholder);

        var editVm = CreateEditVm(section, isNew: true, ctx, section.SemesterId, callerTag: "Add");
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
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

        var ctx = BuildEditorContext();
        if (ctx is null) return;

        var editVm = CreateEditVm(section, isNew, ctx, section.SemesterId, callerTag: "Edit");
        editVm.RequestClose = CollapseEditor;
        ExpandedItem = listItem;
        if (listItem is not null) listItem.IsExpanded = true;
        EditVm = editVm;
    }

    // ── Copy ───────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanWrite))]
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

        var ctx = BuildEditorContext();
        if (ctx is null) return;

        var placeholder = BuildPlaceholder(section, ctx);
        // Insert immediately after the source item
        int insertIndex = SectionItems.IndexOf(afterItem) + 1;
        SectionItems.Insert(insertIndex, placeholder);

        var editVm = CreateEditVm(section, isNew: true, ctx, section.SemesterId, callerTag: "Copy");
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedSection is null) return;
        if (!await _dialog.Confirm($"Delete {SelectedSectionItem!.Heading}?")) return;
        if (ExpandedItem?.Section.Id == SelectedSection.Id)
            CollapseEditor();
        try
        {
            _sectionRepo.Delete(SelectedSection.Id);
            var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
            _sectionStore.Reload(_sectionRepo, semIds); // notifies Grid + Workload via SectionsChanged
            // Load() on this VM is also triggered by the SectionsChanged subscription
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
                _legalStartTimeRepo, _semesterRepo, _blockPatternRepo, _propertyRepo, _campusRepo);

            var sections = generator.GenerateSections(count, semesterId);
            App.Logger.LogInfo($"Generator created {sections.Count} sections", "GenerateRandomSections");

            foreach (var section in sections)
            {
                App.Logger.LogInfo($"Inserting section {section.Id} ({section.SectionCode}) for course {section.CourseId}", "GenerateRandomSections");
                _sectionRepo.Insert(section);
            }

            App.Logger.LogInfo("All sections inserted, reloading store...", "GenerateRandomSections");
            var semIds = _semesterContext.SelectedSemesters.Select(s => s.Semester.Id);
            _sectionStore.Reload(_sectionRepo, semIds); // notifies Grid + Workload + this VM via SectionsChanged

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
