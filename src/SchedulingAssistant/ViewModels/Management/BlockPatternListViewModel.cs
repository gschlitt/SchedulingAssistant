using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Manages up to five block-pattern favourite slots shown in the Block Patterns flyout.
/// Patterns are stored in the database so all users of the same database see the same patterns.
/// </summary>
public partial class BlockPatternListViewModel : ViewModelBase
{
    private readonly BlockPatternRepository _patternRepository;
    private const int MaxSlots = 5;

    public BlockPatternSlotViewModel Slot1 { get; }
    public BlockPatternSlotViewModel Slot2 { get; }
    public BlockPatternSlotViewModel Slot3 { get; }
    public BlockPatternSlotViewModel Slot4 { get; }
    public BlockPatternSlotViewModel Slot5 { get; }

    /// <summary>True while any slot is being edited; used to disable other slot's buttons.</summary>
    public bool IsEditingAny => Slot1.IsEditing || Slot2.IsEditing || Slot3.IsEditing || Slot4.IsEditing || Slot5.IsEditing;

    public BlockPatternListViewModel(BlockPatternRepository patternRepository)
    {
        _patternRepository = patternRepository;
        var includeSaturday = AppSettings.Load().IncludeSaturday;

        var allPatterns = _patternRepository.GetAll();
        var patterns = Enumerable.Range(0, MaxSlots)
            .Select(i => allPatterns.Count > i ? allPatterns[i] : null)
            .ToList();

        Slot1 = new BlockPatternSlotViewModel(1, patterns[0], includeSaturday, _patternRepository, OnSlotEditingChanged);
        Slot2 = new BlockPatternSlotViewModel(2, patterns[1], includeSaturday, _patternRepository, OnSlotEditingChanged);
        Slot3 = new BlockPatternSlotViewModel(3, patterns[2], includeSaturday, _patternRepository, OnSlotEditingChanged);
        Slot4 = new BlockPatternSlotViewModel(4, patterns[3], includeSaturday, _patternRepository, OnSlotEditingChanged);
        Slot5 = new BlockPatternSlotViewModel(5, patterns[4], includeSaturday, _patternRepository, OnSlotEditingChanged);
    }

    private void OnSlotEditingChanged() => OnPropertyChanged(nameof(IsEditingAny));
}

/// <summary>
/// View model for one fixed pattern slot (1 or 2).
/// Handles Edit and Clear in place, persisting immediately to the database.
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
    private readonly BlockPatternRepository _patternRepository;
    private readonly Action _onEditingChanged;

    public BlockPatternSlotViewModel(
        int slotNumber,
        BlockPattern? pattern,
        bool includeSaturday,
        BlockPatternRepository patternRepository,
        Action onEditingChanged)
    {
        SlotNumber = slotNumber;
        _pattern = pattern;
        _includeSaturday = includeSaturday;
        _patternRepository = patternRepository;
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
                if (Pattern?.Id is { } existingId)
                {
                    // Update existing pattern
                    p.Id = existingId;
                    _patternRepository.Update(p);
                }
                else
                {
                    // Insert new pattern
                    _patternRepository.Insert(p);
                }
                Pattern = p;
                EditVm = null;
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand]
    private void Clear()
    {
        if (Pattern?.Id is { } id)
        {
            _patternRepository.Delete(id);
        }
        Pattern = null;
        EditVm = null;
    }
}
