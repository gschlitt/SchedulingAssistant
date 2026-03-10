using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using SchedulingAssistant.ViewModels.GridView;
using System.ComponentModel;

namespace SchedulingAssistant.Views.GridView;

public partial class GridFilterView : UserControl
{
    private GridFilterViewModel? _vm;

    private static IBrush Res(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : Brushes.Transparent;

    private static IBrush ActiveFilterHeaderBrush    => Res("FilterActiveHeader");
    private static IBrush InactiveFilterHeaderBrush  => Res("FilterInactiveHeader");
    private static IBrush ActiveOverlayHeaderBrush   => Res("OverlayActiveHeader");
    private static IBrush InactiveOverlayHeaderBrush => Res("OverlayInactiveHeader");

    public GridFilterView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from the previous VM's events to prevent memory leaks.
        if (_vm is not null)
        {
            _vm.HeadersChanged -= UpdateAllHeaders;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as GridFilterViewModel;

        if (_vm is not null)
        {
            // HeadersChanged fires whenever any filter item's IsSelected toggles, any
            // overlay state changes, or PopulateOptions rebuilds the option lists. A
            // single subscription here replaces the previous 8 SubscribeCollection calls
            // plus the OnCollectionChanged / OnItemChanged per-item subscription pattern.
            _vm.HeadersChanged += UpdateAllHeaders;

            // Also watch specific VM properties for finer overlay updates. HeadersChanged
            // already covers these, but the PropertyChanged subscription is retained for
            // any future code that changes overlay state outside of the commands.
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        UpdateAllHeaders();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GridFilterViewModel.OverlayType) ||
            e.PropertyName == nameof(GridFilterViewModel.SelectedOverlayId))
            UpdateOverlayHeaders();
    }

    // ── Header update logic ──────────────────────────────────────────────────
    // These methods are purely view logic — they set Content, Foreground, and
    // FontWeight on named ToggleButtons based on the current VM filter state.
    // They belong here (not in the VM) because they reference named AXAML controls.

    private void UpdateAllHeaders()
    {
        if (_vm is null) return;
        SetHeader(InstructorToggle,  InstructorPanel,  "Instructor",   _vm.Instructors);
        SetHeader(RoomToggle,        RoomPanel,        "Room",         _vm.Rooms);
        SetHeader(SubjectToggle,     SubjectPanel,     "Subject",      _vm.Subjects);
        SetHeader(CampusToggle,      CampusPanel,      "Campus",       _vm.Campuses);
        SetHeader(SectionTypeToggle, SectionTypePanel, "Section Type", _vm.SectionTypes);
        SetHeader(TagsToggle,        TagsPanel,        "Tags",         _vm.Tags);
        SetHeader(MeetingTypeToggle, MeetingTypePanel, "Meeting Type", _vm.MeetingTypes);
        SetHeader(LevelToggle,       LevelPanel,       "Level",        _vm.Levels);
        UpdateOverlayHeaders();
    }

    private void UpdateOverlayHeaders()
    {
        if (_vm is null) return;
        SetOverlayHeader(InstructorOverlayToggle, InstructorOverlayPanel, "Instructor", "Instructor", _vm.Instructors, _vm);
        SetOverlayHeader(RoomOverlayToggle,       RoomOverlayPanel,       "Room",       "Room",       _vm.Rooms,       _vm);
        SetOverlayHeader(TagOverlayToggle,        TagOverlayPanel,        "Tag",        "Tag",        _vm.Tags,        _vm);
    }

    /// <summary>
    /// Updates a filter dimension's header ToggleButton to show how many items are
    /// selected, with active colouring when any are.
    /// </summary>
    /// <param name="toggle">The ToggleButton whose label, colour, and weight to update.</param>
    /// <param name="panel">The Panel containing the toggle (controls IsVisible).</param>
    /// <param name="dimensionName">Human-readable label for this filter dimension.</param>
    /// <param name="items">The filter items for this dimension.</param>
    private static void SetHeader(
        ToggleButton toggle,
        Panel panel,
        string dimensionName,
        IEnumerable<FilterItemViewModel> items)
    {
        panel.IsVisible = true;
        var list = items.ToList();
        int selected = list.Count(i => i.IsSelected);
        toggle.Content    = selected > 0 ? $"{dimensionName} ({selected}) ▾" : $"{dimensionName} ▾";
        toggle.Foreground = selected > 0 ? ActiveFilterHeaderBrush : InactiveFilterHeaderBrush;
        toggle.FontWeight = selected > 0 ? FontWeight.SemiBold : FontWeight.Normal;
    }

    /// <summary>
    /// Updates an overlay dimension's header ToggleButton. Shows the currently active
    /// overlay name when one is selected; hides the panel when no named items exist.
    /// </summary>
    /// <param name="toggle">The ToggleButton to update.</param>
    /// <param name="panel">The Panel containing the toggle (controls IsVisible).</param>
    /// <param name="inactiveLabel">Label displayed when no overlay is active.</param>
    /// <param name="overlayTypeName">The overlay type string stored in the VM (e.g. "Instructor").</param>
    /// <param name="items">Items in this overlay dimension.</param>
    /// <param name="vm">The filter VM (provides overlay state).</param>
    private void SetOverlayHeader(
        ToggleButton toggle,
        Panel panel,
        string inactiveLabel,
        string overlayTypeName,
        IEnumerable<FilterItemViewModel> items,
        GridFilterViewModel vm)
    {
        var list = items.ToList();

        // Only show the overlay panel when there are actual named items (sentinels don't count).
        int namedCount = list.Count(i => i.Id != GridFilterViewModel.NotStaffedId
                                      && i.Id != GridFilterViewModel.UnroomedId);
        if (namedCount == 0)
        {
            panel.IsVisible = false;
            return;
        }

        panel.IsVisible = true;
        bool isActive = vm.OverlayType == overlayTypeName && vm.HasOverlay;

        if (isActive)
        {
            var name = list.FirstOrDefault(i => i.Id == vm.SelectedOverlayId)?.Name ?? vm.SelectedOverlayId;
            toggle.Content    = $"Overlay: {name} ▾";
            toggle.Foreground = ActiveOverlayHeaderBrush;
            toggle.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            toggle.Content    = $"{inactiveLabel} ▾";
            toggle.Foreground = InactiveOverlayHeaderBrush;
            toggle.FontWeight = FontWeight.Normal;
        }
    }
}
