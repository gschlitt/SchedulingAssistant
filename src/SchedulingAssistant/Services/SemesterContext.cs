using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.Services;

/// <summary>
/// Pairs a Semester with a formatted display label.
/// <see cref="DisplayName"/> uses the full "Year — Semester" form for legacy contexts.
/// For the semester picker UI, prefer <see cref="SemesterCheckItem.DisplayName"/> (short form).
/// </summary>
public class SemesterDisplay
{
    public required Semester Semester { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Wraps a <see cref="SemesterDisplay"/> for use in the semester picker popup.
/// Each item independently tracks its selected state; toggling it notifies the parent
/// <see cref="SemesterContext"/> via the internal <see cref="SelectionChanged"/> event
/// so the context can rebuild <see cref="SemesterContext.SelectedSemesters"/>.
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
    /// Sets <see cref="IsSelected"/> and notifies the UI without firing
    /// <see cref="SelectionChanged"/>.  Used by <see cref="SemesterContext"/> when it needs
    /// to force-restore a selection without triggering a re-entrant callback.
    /// </summary>
    internal void SetSelectedSilent(bool value)
    {
        if (SetProperty(ref _isSelected, value))
            OnPropertyChanged(nameof(IsSelected));
    }

    /// <summary>
    /// Fired when <see cref="IsSelected"/> changes.
    /// <see cref="SemesterContext"/> wires this on construction of each item.
    /// </summary>
    internal event Action<SemesterCheckItem>? SelectionChanged;

    /// <param name="semDisplay">The semester this item wraps.</param>
    /// <param name="isSelected">
    /// Initial selected state.  Set directly (bypassing the property setter)
    /// so no <see cref="SelectionChanged"/> event fires during construction.
    /// </param>
    public SemesterCheckItem(SemesterDisplay semDisplay, bool isSelected = false)
    {
        SemDisplay = semDisplay;
        _isSelected = isSelected;
    }
}

/// <summary>
/// Singleton service holding the globally-selected academic year and semester(s).
/// Supports selecting multiple semesters within one academic year simultaneously.
/// <para>
/// ViewModels subscribe to:
/// <list type="bullet">
///   <item><see cref="SelectedSemesterDisplay"/> — single-semester backward compatibility</item>
///   <item><see cref="SelectedSemesters"/> — full multi-semester awareness</item>
/// </list>
/// Both fire <c>PropertyChanged</c> whenever the selection changes.
/// </para>
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
    /// Bind <c>ItemsSource</c> in the UI to this collection.
    /// Each item exposes <see cref="SemesterCheckItem.IsSelected"/> for two-way binding.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SemesterCheckItem> _filteredCheckItems = new();

    /// <summary>
    /// The semesters currently selected for display (one or more).
    /// Derived from the checked items in <see cref="FilteredCheckItems"/>;
    /// rebuilt whenever a check item toggles.
    /// Fires <c>PropertyChanged</c> on every rebuild.
    /// </summary>
    public IReadOnlyList<SemesterDisplay> SelectedSemesters { get; private set; } = [];

    /// <summary>
    /// Label for the semester picker button, e.g. "Fall 2025" or "Early Summer, Summer".
    /// "(none)" when nothing is selected (should not normally occur).
    /// </summary>
    public string SelectedSemestersLabel { get; private set; } = "(none)";

    /// <summary>True when more than one semester is currently selected.</summary>
    public bool IsMultiSemesterMode => SelectedSemesters.Count > 1;

    /// <summary>Controls whether the semester picker Popup is open.</summary>
    [ObservableProperty]
    private bool _isSemesterPickerOpen;

    // ── Backward-Compatible Single-Semester Surface ────────────────────────────

    /// <summary>
    /// The primary (first) selected semester, or null if none selected.
    /// ViewModels that only support a single semester (WorkloadPanel, CommitmentsManagement,
    /// etc.) subscribe to this property and continue to work unchanged.
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
    /// Semesters in the selected year as plain <see cref="SemesterDisplay"/> objects (no
    /// selection state).  Kept for backward compatibility; prefer
    /// <see cref="FilteredCheckItems"/> in new code.
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
    /// Falls back to the first year / first semester when previous selections are gone.
    /// </summary>
    /// <param name="ayRepo">Academic year repository.</param>
    /// <param name="semRepo">Semester repository.</param>
    public void Reload(AcademicYearRepository ayRepo, SemesterRepository semRepo)
    {
        var previousYearId      = SelectedAcademicYear?.Id;
        var previousSemesterIds = SelectedSemesters.Select(s => s.Semester.Id).ToHashSet();

        var allYears     = ayRepo.GetAll().OrderBy(y => y.StartYear).ToList();
        var allSemesters = semRepo.GetAll();

        _yearLookup      = allYears.ToDictionary(y => y.Id);
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
            // Set the backing field directly to suppress the OnSelectedAcademicYearChanged
            // partial void — we call RebuildCheckItems ourselves with restoration IDs.
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
    /// Called by the CommunityToolkit-generated setter when <see cref="SelectedAcademicYear"/>
    /// changes interactively.  Rebuilds check items and auto-selects the first semester.
    /// </summary>
    partial void OnSelectedAcademicYearChanged(AcademicYear? oldValue, AcademicYear? newValue)
    {
        // Year changed interactively — drop previous semester selection, auto-select first
        RebuildCheckItems(previousIds: null);
    }

    /// <summary>
    /// Rebuilds <see cref="FilteredCheckItems"/> for the current academic year.
    /// Detaches old event handlers, creates new items, restores or auto-selects,
    /// then calls <see cref="UpdateSelectedSemesters"/>.
    /// </summary>
    /// <param name="previousIds">
    /// Semester IDs to restore as selected.
    /// Pass <c>null</c> to auto-select only the first semester.
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

        // Build items, marking any previously-selected ones
        var newItems = filtered.Select(d =>
        {
            bool selected = previousIds != null && previousIds.Contains(d.Semester.Id);
            return new SemesterCheckItem(d, selected);
        }).ToList();

        // If nothing was restored (or previousIds was null), auto-select the first semester
        if (newItems.Count > 0 && !newItems.Any(ci => ci.IsSelected))
            newItems[0].SetSelectedSilent(true);

        // Wire new event handlers before assigning the collection
        foreach (var ci in newItems)
            ci.SelectionChanged += OnCheckItemSelectionChanged;

        FilteredCheckItems = new ObservableCollection<SemesterCheckItem>(newItems);
        UpdateSelectedSemesters();
    }

    /// <summary>
    /// Called whenever a <see cref="SemesterCheckItem.IsSelected"/> changes.
    /// Prevents the last selected semester from being deselected (at least one must remain),
    /// then rebuilds <see cref="SelectedSemesters"/> and fires downstream notifications.
    /// </summary>
    private void OnCheckItemSelectionChanged(SemesterCheckItem changed)
    {
        if (!changed.IsSelected)
        {
            // Count how many other items are still selected
            int otherSelected = FilteredCheckItems.Count(ci => ci != changed && ci.IsSelected);
            if (otherSelected == 0)
            {
                // Cannot deselect the last semester — silently restore it
                changed.SetSelectedSilent(true);
                return;
            }
        }

        UpdateSelectedSemesters();
    }

    /// <summary>
    /// Rebuilds <see cref="SelectedSemesters"/> from the checked items and fires all
    /// downstream <c>PropertyChanged</c> notifications so subscribed ViewModels reload.
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
    }
}
