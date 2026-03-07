using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using SchedulingAssistant.ViewModels.GridView;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SchedulingAssistant.Views.GridView;

public partial class GridFilterView : UserControl
{
    private GridFilterViewModel? _vm;

    private static IBrush Res(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : Brushes.Transparent;

    private static IBrush ActiveFilterHeaderBrush   => Res("FilterActiveHeader");
    private static IBrush InactiveFilterHeaderBrush => Res("FilterInactiveHeader");

    private static IBrush ActiveOverlayHeaderBrush => Res("OverlayActiveHeader");
    private static IBrush InactiveOverlayHeaderBrush => Res("OverlayInactiveHeader");

    public GridFilterView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as GridFilterViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;

            // Subscribe to collection changes for each dimension to update headers live.
            SubscribeCollection(_vm.Instructors);
            SubscribeCollection(_vm.Rooms);
            SubscribeCollection(_vm.Subjects);
            SubscribeCollection(_vm.Campuses);
            SubscribeCollection(_vm.SectionTypes);
            SubscribeCollection(_vm.Tags);
            SubscribeCollection(_vm.MeetingTypes);
            SubscribeCollection(_vm.Levels);
        }

        UpdateAllHeaders();
        WireUpOverlayListBoxes();
    }

    private void WireUpOverlayListBoxes()
    {
        if (this.FindControl<ListBox>("InstructorOverlayListBox") is { } instrLb)
            instrLb.SelectionChanged += OnInstructorOverlayChanged;
        if (this.FindControl<ListBox>("RoomOverlayListBox") is { } roomLb)
            roomLb.SelectionChanged += OnRoomOverlayChanged;
        if (this.FindControl<ListBox>("TagOverlayListBox") is { } tagLb)
            tagLb.SelectionChanged += OnTagOverlayChanged;
    }

    private void OnInstructorOverlayChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        var selectedId = (e.AddedItems[0] is FilterItemViewModel item) ? item.Id : null;

        // Clear selection BEFORE executing the command to prevent Avalonia's selection
        // model from crashing when the Instructors collection is rebuilt (list.Clear()).
        if (sender is ListBox lb)
            lb.SelectedItem = null;
        InstructorOverlayToggle.IsChecked = false;

        if (_vm?.SetInstructorOverlayCommand.CanExecute(selectedId) == true)
            _vm.SetInstructorOverlayCommand.Execute(selectedId);
    }

    private void OnRoomOverlayChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        var selectedId = (e.AddedItems[0] is FilterItemViewModel item) ? item.Id : null;

        if (sender is ListBox lb)
            lb.SelectedItem = null;
        RoomOverlayToggle.IsChecked = false;

        if (_vm?.SetRoomOverlayCommand.CanExecute(selectedId) == true)
            _vm.SetRoomOverlayCommand.Execute(selectedId);
    }

    private void OnTagOverlayChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        var selectedId = (e.AddedItems[0] is FilterItemViewModel item) ? item.Id : null;

        if (sender is ListBox lb)
            lb.SelectedItem = null;
        TagOverlayToggle.IsChecked = false;

        if (_vm?.SetTagOverlayCommand.CanExecute(selectedId) == true)
            _vm.SetTagOverlayCommand.Execute(selectedId);
    }

    private void SubscribeCollection(System.Collections.ObjectModel.ObservableCollection<FilterItemViewModel> col)
    {
        col.CollectionChanged += OnCollectionChanged;
        foreach (var item in col)
            item.PropertyChanged += OnItemChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (FilterItemViewModel item in e.NewItems)
                item.PropertyChanged += OnItemChanged;
        if (e.OldItems is not null)
            foreach (FilterItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnItemChanged;

        UpdateAllHeaders();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterItemViewModel.IsSelected))
            UpdateAllHeaders();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GridFilterViewModel.OverlayType) ||
            e.PropertyName == nameof(GridFilterViewModel.SelectedOverlayId))
            UpdateOverlayHeaders();
    }

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

    private void SetOverlayHeader(
        ToggleButton toggle,
        Panel panel,
        string inactiveLabel,
        string overlayTypeName,
        IEnumerable<FilterItemViewModel> items,
        GridFilterViewModel vm)
    {
        var list = items.ToList();
        // Only show overlay panel when there are actual named items (sentinels don't count)
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

    //the filter headers, specifically
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
}
