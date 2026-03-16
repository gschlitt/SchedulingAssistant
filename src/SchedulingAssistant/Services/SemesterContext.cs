using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.Services;

/// <summary>
/// Pairs a Semester with a formatted display label.
/// DisplayName uses the full "Year — Semester" form for legacy contexts;
/// for the semester picker UI prefer <see cref="SemesterCheckItem.DisplayName"/> (short form).
/// </summary>
public class SemesterDisplay
{
    public required Semester Semester { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Wraps a <see cref="SemesterDisplay"/> for the semester picker popup.
/// Each item tracks its own selected state; toggling it notifies the parent
/// <see cref="SemesterContext"/> via the internal <see cref="SelectionChanged"/> event.
/// </summary>
public class SemesterCheckItem : ObservableObject
{
    /// <summary>The underlying semester data.</summary>
    public SemesterDisplay SemDisplay { get; }

    /// <summary>Short semester name shown in the checkbox list (e.g. "Fall 2025").</summary>
    public string DisplayName => SemDisplay.Semester.Name;

    private bool _isSelected;

    /// <summary>Whether this semester is currently active in the multi-semester view.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Sets IsSelected and updates the UI, but does NOT fire <see cref="SelectionChanged"/>.
    /// Used by <see cref="SemesterContext"/> to forcibly re-select the last semester
    /// without causing a re-entrant selection-changed callback.
    /// </summary>
    internal void SetSelectedSilent(bool value)
    {
        if (SetProperty(ref _isSelected, value))
            OnPropertyChanged(nameof(IsSelected));
    }

    /// <summary>
    /// Fired when <see cref="IsSelected"/> changes.
    /// Wired by <see cref="SemesterContext"/> to rebuild <see cref="SemesterContext.SelectedSemesters"/>.
    /// </summary>
    internal event Action<SemesterCheckItem>? SelectionChanged;

    /// <param name="semDisplay">The semester this item wraps.</param>
    /// <param name="isSelected">Initial selected state; set directly to avoid firing the event.</param>
    public SemesterCheckItem(SemesterDisplay semDisplay, bool isSelected = false)
    {
        SemDisplay = semDisplay;
        _isSelected = isSelected;
    }
}

/// <summary>
/// Singleton service that holds the globally-selected academic year and semester(s).
/// Supports multi-semester selection: multiple semesters within one academic year can be
/// active simultaneously. ViewModels subscribe to:
/// <list type="bullet">
///   <item><see cref="SelectedSemesterDisplay"/> for single-semester backward compatibility</item>
///   <item><see cref="SelectedSemesters"/> for full multi-semester awareness</item>
/// </list>
/// </summary>
public partial class SemesterContext : ObservableObject
{
    // ── Academic Year ──────────────────────────────────────────────────────────

    /// <summary>All academic years loaded from the database.</summary>
    [ObservableProperty]
    private ObservableCollection<AcademicYear> _academicYears = new();

    /// <summary>The currently selected academic year; drives the semester picker list.</summary>
    [ObservableProperty]
    private AcademicYear? _selectedAcademicYear;

    // ── Multi-Semester Selection ───────────────────────────────────────────────

    /// <summary>
    /// Checkbox items for the semester picker popup — one per semester in the selected year.
    /// Bind <c>ItemsSource</c> to this; each item exposes <c>IsSelected</c> two-way.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SemesterCheckItem> _filteredCheckItems = new();

    /// <summary>
    /// The semesters currently selected for display (one or more).
    /// Derived from <see cref="FilteredCheckItems"/>; rebuilt whenever a check item toggles.
    /// </summary>
    public IReadOnlyList<SemesterDisplay> SelectedSemesters { get; private set; } = [];

    /// <summary>
    /// Text for the semester picker button.
    /// Shows a comma-separated list of semester names, e.g. "Fall 2025" or "Early Summer, Summer".
    /// </summary>
    public string SelectedSemestersLabel { get; private set; } = "(none)";

    /// <summary>True when more than one semester is currently selected.</summary>
    public bool IsMultiSemesterMode => SelectedSemesters.Count > 1;

    /// <summary>Controls whether the semester picker Popup is open.</summary>
    [ObservableProperty]
    private bool _isSemesterPickerOpen;

    // ── Backward-Compatible Single-Semester Surface ────────────────────────────

    /// <summary>
    /// The primary (first) selected semester, or null if none are selected.
    /// ViewModels that only support a single semester subscribe to this property.
    /// Fires <c>PropertyChanged</c> whenever <see cref="SelectedSemesters"/> changes.
    /// </summary>
    public SemesterDisplay? SelectedSemesterDisplay => SelectedSemesters.FirstOrDefault();

    // ── Legacy Surface ─────────────────────────────────────────────────────────

    /// <summary>
    /// Flat list of all semesters with full "Year — Semester" display names.
    /// Kept for backward compatibility; prefer <see cref="FilteredCheckItems"/> in new code.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SemesterDisplay> _semesterDisplays = new();

    /// <summary>
    /// Semesters in the selected year as <see cref="SemesterDisplay"/> objects (no IsSelected state).
    /// Kept for backward compatibility; prefer <see cref="FilteredCheckItems"/> in new code.
    /// </summary>
    public IReadOnlyList<SemesterDisplay> FilteredSemesters =>
        FilteredCheckItems.Select(ci => ci.SemDisplay).ToList();

    // ── Internal Lookups ───────────────────────────────────────────────────────

    private Dictionary<string, AcademicYear> _yearLookup = new();
    private Dictionary<string, List<SemesterDisplay>> _semestersByYear = new();

    // ── Commands ───────────────────────────────────────────────────────────────

    /// <summary>Toggles the semester picker Popup open or closed.</summary>
    [RelayCommand]
    private void ToggleSemesterPicker() => IsSemesterPickerOpen = !IsSemesterPickerOpen;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reloads academic years and semesters from the database.
    /// Preserves the previously-selected academic year and semester(s) if they still exist.
    /// Falls back to the first year / first semester when the previous selection is gone.
    /// </summary>
    /// <param name="ayRepo">Academic year repository.</param>
    /// <param name="semRepo">Semester repository.</param>
    /// <param name="restoreAcademicYearId">
    /// When provided, overrides the in-memory selection for restoration (e.g. startup restore
    /// from <see cref="AppSettings"/>). Pass <c>null</c> to preserve the current selection.
    /// </param>
    /// <param name="restoreSemesterIds">
    /// When provided, overrides the in-memory semester selection for restoration. Paired with
    /// <paramref name="restoreAcademicYearId"/>. Pass <c>null</c> to preserve current selection.
    /// </param>
    public void Reload(AcademicYearRepository ayRepo, SemesterRepository semRepo,
        string? restoreAcademicYearId = null, IReadOnlySet<string>? restoreSemesterIds = null)
    {
        var previousYearId      = restoreAcademicYearId ?? SelectedAcademicYear?.Id;
        var previousSemesterIds = restoreSemesterIds    ?? SelectedSemesters.Select(s => s.Semester.Id).ToHashSet();

        var allYears     = ayRepo.GetAll().OrderBy(y => y.StartYear).ToList();
        var allSemesters = semRepo.GetAll();

        // Rebuild lookup tables
        _yearLookup     = allYears.ToDictionary(y => y.Id);
        _semestersByYear = new Dictionary<string, List<SemesterDisplay>>();

        var allDisplays = new List<SemesterDisplay>();
        foreach (var semester in allSemesters)
        {
            var display = new SemesterDisplay
            {
                Semester    = semester,
                DisplayName = _yearLookup.TryGetValue(semester.AcademicYearId, out var year)
                    ? $"{year.Name} — {semester.Name}"
                    : semester.Name
            };
            allDisplays.Add(display);

            if (!_semestersByYear.ContainsKey(semester.AcademicYearId))
                _semestersByYear[semester.AcademicYearId] = new();
            _semestersByYear[semester.AcademicYearId].Add(display);
        }

        SemesterDisplays = new ObservableCollection<SemesterDisplay>(allDisplays);
        AcademicYears    = new ObservableCollection<AcademicYear>(allYears);

        var newSelectedYear = allYears.FirstOrDefault(y => y.Id == previousYearId)
                              ?? allYears.FirstOrDefault();

        if (newSelectedYear != null)
        {
            // Assign directly to suppress the OnSelectedAcademicYearChanged partial;
            // we call RebuildCheckItems ourselves with restoration IDs.
            _selectedAcademicYear = newSelectedYear;
            OnPropertyChanged(nameof(SelectedAcademicYear));
            RebuildCheckItems(previousSemesterIds);
        }
        else
        {
            SelectedAcademicYear = null;
            FilteredCheckItems   = new ObservableCollection<SemesterCheckItem>();
            UpdateSelectedSemesters();
        }
    }

    // ── Internal Mechanics ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by the CommunityToolkit-generated setter when <see cref="SelectedAcademicYear"/> changes.
    /// Rebuilds the semester check items and auto-selects the first semester in the new year.
    /// </summary>
    partial void OnSelectedAcademicYearChanged(AcademicYear? oldValue, AcademicYear? newValue)
    {
        // Year changed interactively — drop old selection, auto-select first semester
        RebuildCheckItems(previousIds: null);
    }

    /// <summary>
    /// Rebuilds <see cref="FilteredCheckItems"/> for the current academic year.
    /// </summary>
    /// <param name="previousIds">
    /// Semester IDs to restore as selected. Pass <c>null</c> to auto-select the first semester only.
    /// </param>
    private void RebuildCheckItems(IReadOnlySet<string>? previousIds)
    {
        // Detach old event handlers to avoid leaks
        foreach (var old in FilteredCheckItems)
            old.SelectionChanged -= OnCheckItemSelectionChanged;

        if (SelectedAcademicYear == null)
        {
            FilteredCheckItems = new ObservableCollection<SemesterCheckItem>();
            UpdateSelectedSemesters();
            return;
        }

        var filtered = _semestersByYear.TryGetValue(SelectedAcademicYear.Id, out var list)
            ? list
            : new List<SemesterDisplay>();

        // Build items, marking restorable ones as selected
        var newItems = filtered.Select(d =>
        {
            bool selected = previousIds != null && previousIds.Contains(d.Semester.Id);
            return new SemesterCheckItem(d, selected);
        }).ToList();

        // If nothing was restored (or no previousIds), auto-select the first semester
        if (newItems.Count > 0 && !newItems.Any(ci => ci.IsSelected))
            newItems[0].SetSelectedSilent(true);

        // Wire new event handlers
        foreach (var ci in newItems)
            ci.SelectionChanged += OnCheckItemSelectionChanged;

        FilteredCheckItems = new ObservableCollection<SemesterCheckItem>(newItems);
        UpdateSelectedSemesters();
    }

    /// <summary>
    /// Called when any <see cref="SemesterCheckItem.IsSelected"/> changes.
    /// Prevents the last selected semester from being deselected.
    /// Then rebuilds <see cref="SelectedSemesters"/> and fires change notifications.
    /// </summary>
    /// <param name="changed">The item whose selection state just changed.</param>
    private void OnCheckItemSelectionChanged(SemesterCheckItem changed)
    {
        // Guard: at least one semester must remain selected
        if (!changed.IsSelected)
        {
            int otherSelected = FilteredCheckItems.Count(ci => ci != changed && ci.IsSelected);
            if (otherSelected == 0)
            {
                // Silently re-select; avoid re-entrant SelectionChanged
                changed.SetSelectedSilent(true);
                return;
            }
        }

        UpdateSelectedSemesters();
    }

    /// <summary>
    /// Rebuilds <see cref="SelectedSemesters"/> from the checked items, fires
    /// all downstream PropertyChanged notifications so subscribed ViewModels reload,
    /// and persists the new selection to <see cref="AppSettings"/> for startup restore.
    /// </summary>
    private void UpdateSelectedSemesters()
    {
        SelectedSemesters = FilteredCheckItems
            .Where(ci => ci.IsSelected)
            .Select(ci => ci.SemDisplay)
            .ToList();

        SelectedSemestersLabel = SelectedSemesters.Count == 0
            ? "(none)"
            : string.Join(", ", SelectedSemesters.Select(s => s.Semester.Name));

        OnPropertyChanged(nameof(SelectedSemesters));
        OnPropertyChanged(nameof(SelectedSemestersLabel));
        OnPropertyChanged(nameof(IsMultiSemesterMode));
        OnPropertyChanged(nameof(FilteredSemesters));
        // Notifies all single-semester ViewModels (WorkloadPanel, CommitmentsManagement, etc.)
        OnPropertyChanged(nameof(SelectedSemesterDisplay));

        // Persist the selection so it can be restored on the next startup.
        var s = AppSettings.Current;
        s.LastSelectedAcademicYearId  = SelectedAcademicYear?.Id;
        s.LastSelectedSemesterIds     = SelectedSemesters.Select(sd => sd.Semester.Id).ToList();
        s.Save();
    }
}
