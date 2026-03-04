using CommunityToolkit.Mvvm.ComponentModel;

namespace SchedulingAssistant.ViewModels.GridView;

public partial class ContextMenuItemVm : ObservableObject
{
    public string Id { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    public ContextMenuItemVm(string id, string label, bool isChecked = false)
    {
        Id = id;
        Label = label;
        _isChecked = isChecked;
    }
}
