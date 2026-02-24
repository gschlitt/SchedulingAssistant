using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Manages the two fixed block-pattern favourite slots shown in the Block Patterns flyout.
/// Patterns are stored in AppSettings (not the DB) as they are app-level preferences.
/// </summary>
public partial class BlockPatternListViewModel : ViewModelBase
{
    public BlockPatternSlotViewModel Slot1 { get; }
    public BlockPatternSlotViewModel Slot2 { get; }

    /// <summary>True while either slot is being edited; used to disable the other slot's buttons.</summary>
    public bool IsEditingAny => Slot1.IsEditing || Slot2.IsEditing;

    public BlockPatternListViewModel()
    {
        var settings = AppSettings.Load();
        var includeSaturday = settings.IncludeSaturday;

        Slot1 = new BlockPatternSlotViewModel(1, settings.Pattern1, includeSaturday, OnSlotEditingChanged);
        Slot2 = new BlockPatternSlotViewModel(2, settings.Pattern2, includeSaturday, OnSlotEditingChanged);
    }

    private void OnSlotEditingChanged() => OnPropertyChanged(nameof(IsEditingAny));
}

/// <summary>
/// View model for one fixed pattern slot (1 or 2).
/// Handles Edit and Clear in place, persisting immediately to AppSettings.
/// </summary>
public partial class BlockPatternSlotViewModel : ViewModelBase
{
    public int SlotNumber { get; }

    [ObservableProperty] private BlockPattern? _pattern;
    [ObservableProperty] private BlockPatternEditViewModel? _editVm;

    /// <summary>True while the inline editor is open for this slot.</summary>
    public bool IsEditing => EditVm is not null;

    /// <summary>Display name: the pattern name, or "(not set)" when empty.</summary>
    public string DisplayName => Pattern?.Name is { Length: > 0 } n ? n : "(not set)";

    private readonly bool _includeSaturday;
    private readonly Action _onEditingChanged;

    public BlockPatternSlotViewModel(
        int slotNumber,
        BlockPattern? pattern,
        bool includeSaturday,
        Action onEditingChanged)
    {
        SlotNumber = slotNumber;
        _pattern = pattern;
        _includeSaturday = includeSaturday;
        _onEditingChanged = onEditingChanged;
    }

    partial void OnEditVmChanged(BlockPatternEditViewModel? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        _onEditingChanged();
    }

    partial void OnPatternChanged(BlockPattern? value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    [RelayCommand]
    private void Edit()
    {
        EditVm = new BlockPatternEditViewModel(
            SlotNumber,
            Pattern,
            _includeSaturday,
            onSave: p =>
            {
                Pattern = p;
                EditVm = null;
                var settings = AppSettings.Load();
                if (SlotNumber == 1) settings.Pattern1 = p;
                else                 settings.Pattern2 = p;
                settings.Save();
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Clear()
    {
        Pattern = null;
        EditVm = null;
        var settings = AppSettings.Load();
        if (SlotNumber == 1) settings.Pattern1 = null;
        else                 settings.Pattern2 = null;
        settings.Save();
    }
}
