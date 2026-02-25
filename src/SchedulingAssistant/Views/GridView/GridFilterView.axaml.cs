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

    private static IBrush ActiveHeaderBrush   => Res("FilterActiveHeader");
    private static IBrush InactiveHeaderBrush => Res("FilterInactiveHeader");

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
        }

        UpdateAllHeaders();
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
        // Nothing extra needed here; item-level changes cover header updates.
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
    }

    private static void SetHeader(
        ToggleButton toggle,
        Panel panel,
        string dimensionName,
        IEnumerable<FilterItemViewModel> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            // No options available — hide the button+popup panel entirely
            panel.IsVisible = false;
            return;
        }

        panel.IsVisible = true;
        int selected = list.Count(i => i.IsSelected);
        toggle.Content    = selected > 0 ? $"{dimensionName} ({selected}) ▾" : $"{dimensionName} ▾";
        toggle.Foreground = selected > 0 ? ActiveHeaderBrush : InactiveHeaderBrush;
        toggle.FontWeight = selected > 0 ? FontWeight.SemiBold : FontWeight.Normal;
    }
}
