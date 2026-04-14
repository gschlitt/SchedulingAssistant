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
public partial class BlockPatternListViewModel : ViewModelBase, IDismissableEditor
{
    private readonly IBlockPatternRepository _patternRepository;
    private readonly WriteLockService _lockService;
    private const int MaxSlots = 5;

    /// <summary>All pattern slots, indexed 0–(MaxSlots-1). Increase <see cref="MaxSlots"/> to add more.</summary>
    public IReadOnlyList<BlockPatternSlotViewModel> Slots { get; }

    /// <summary>True while any slot is being edited; used to disable other slots' buttons.</summary>
    public bool IsEditingAny => Slots.Any(s => s.IsEditing);

    /// <summary>True when the current user holds the write lock; controls whether Edit/Clear buttons are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    public BlockPatternListViewModel(IBlockPatternRepository patternRepository, WriteLockService lockService)
    {
        _patternRepository = patternRepository;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;

        var includeSaturday = AppSettings.Current.IncludeSaturday;
        var includeSunday   = AppSettings.Current.IncludeSunday;

        var allPatterns = _patternRepository.GetAll();
        var patterns = Enumerable.Range(0, MaxSlots)
            .Select(i => allPatterns.Count > i ? allPatterns[i] : null)
            .ToList();

        Slots = Enumerable.Range(0, MaxSlots)
            .Select(i => new BlockPatternSlotViewModel(i + 1, patterns[i], includeSaturday, includeSunday, _patternRepository, OnSlotEditingChanged, () => _lockService.IsWriter))
            .ToList();

    }

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        var editing = Slots.FirstOrDefault(s => s.IsEditing);
        if (editing?.EditVm is null) return false;
        editing.EditVm.CancelCommand.Execute(null);
        return true;
    }

    private void OnSlotEditingChanged() => OnPropertyChanged(nameof(IsEditingAny));

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states on all slots.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        foreach (var slot in Slots)
        {
            slot.EditCommand.NotifyCanExecuteChanged();
            slot.ClearCommand.NotifyCanExecuteChanged();
        }
    }
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

    /// <summary>Heading label for this slot, e.g. "Pattern 1".</summary>
    public string Label => $"Pattern {SlotNumber}";

    /// <summary>Display name: the pattern name, or "(not set)" when empty.</summary>
    public string DisplayName => Pattern?.Name is { Length: > 0 } n ? n : "(not set)";

    private readonly bool _includeSaturday;
    private readonly bool _includeSunday;
    private readonly IBlockPatternRepository _patternRepository;
    private readonly Action _onEditingChanged;
    private readonly Func<bool> _canWrite;

    public BlockPatternSlotViewModel(
        int slotNumber,
        BlockPattern? pattern,
        bool includeSaturday,
        bool includeSunday,
        IBlockPatternRepository patternRepository,
        Action onEditingChanged,
        Func<bool> canWrite)
    {
        SlotNumber = slotNumber;
        _pattern = pattern;
        _includeSaturday = includeSaturday;
        _includeSunday = includeSunday;
        _patternRepository = patternRepository;
        _onEditingChanged = onEditingChanged;
        _canWrite = canWrite;
    }

    /// <summary>Returns true when the parent write lock is held. Used as a CanExecute predicate for Edit and Clear commands.</summary>
    private bool CanWrite() => _canWrite();

    partial void OnEditVmChanged(BlockPatternEditViewModel? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        _onEditingChanged();
    }

    partial void OnPatternChanged(BlockPattern? value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        EditVm = new BlockPatternEditViewModel(
            SlotNumber,
            Pattern,
            _includeSaturday,
            _includeSunday,
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

    [RelayCommand(CanExecute = nameof(CanWrite))]
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
