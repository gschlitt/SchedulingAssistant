using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Campuses settings panel. Supports full CRUD and manual ordering.
/// </summary>
public partial class CampusListViewModel : ViewModelBase
{
    private readonly ICampusRepository _repo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    /// <summary>Label shown in the Scheduling Environment sidebar nav.</summary>
    public string DisplayName => "Campuses";

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<Campus> _items = new();
    [ObservableProperty] private Campus? _selectedItem;
    [ObservableProperty] private CampusEditViewModel? _editVm;

    /// <param name="repo">Campus repository for CRUD operations.</param>
    /// <param name="dialog">Service for confirmation and error dialogs.</param>
    /// <param name="lockService">Write lock; gates edit operations in read-only mode.</param>
    public CampusListViewModel(
        ICampusRepository repo,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _repo = repo;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    /// <summary>Reloads the campus list from the database.</summary>
    public void Load() =>
        Items = new ObservableCollection<Campus>(_repo.GetAll());

    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    /// <summary>Opens the Add form for a new campus.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var campus = new Campus
        {
            SortOrder = Items.Count > 0 ? Items.Max(c => c.SortOrder) + 1 : 0
        };
        EditVm = new CampusEditViewModel(campus, isNew: true,
            onSave: c => { _repo.Insert(c); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(name));
    }

    /// <summary>Opens the Edit form for the currently selected campus.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedItem is null) return;
        var copy = new Campus
        {
            Id           = SelectedItem.Id,
            Name         = SelectedItem.Name,
            Abbreviation = SelectedItem.Abbreviation,
            SortOrder    = SelectedItem.SortOrder,
        };
        EditVm = new CampusEditViewModel(copy, isNew: false,
            onSave: c => { _repo.Update(c); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(name, excludeId: copy.Id));
    }

    /// <summary>
    /// Prompts for confirmation and deletes the selected campus.
    /// Note: does NOT scrub CampusId references from rooms, sections, or prefixes —
    /// that cleanup is handled by the "Prune deleted properties" pending work item.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedItem is null) return;

        if (!await _dialog.Confirm($"Delete campus \"{SelectedItem.Name}\"?"))
            return;

        _repo.Delete(SelectedItem.Id);
        Load();
    }
}

/// <summary>
/// Inline Add/Edit form ViewModel for a single <see cref="Campus"/>.
/// Communicates results back to the parent list via callbacks.
/// </summary>
public partial class CampusEditViewModel : ViewModelBase
{
    private readonly Campus _target;
    private readonly Action<Campus> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _nameExists;

    /// <summary>True when adding a new campus; false when editing.</summary>
    public bool IsNew { get; }

    /// <summary>Form title: "Add Campus" or "Edit Campus".</summary>
    public string Title => IsNew ? "Add Campus" : "Edit Campus";

    /// <summary>Full display name of the campus.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    /// <summary>Short abbreviation used in section code generation (optional).</summary>
    [ObservableProperty] private string _abbreviation = string.Empty;

    /// <summary>
    /// Validation error shown beneath the Name field, or null when the input is valid.
    /// </summary>
    public string? ValidationError
    {
        get
        {
            var trimmed = Name.Trim();
            if (trimmed.Length == 0) return null;
            if (_nameExists(trimmed)) return $"\"{trimmed}\" already exists.";
            return null;
        }
    }

    private bool CanSave() => Name.Trim().Length > 0 && ValidationError is null;

    /// <param name="target">The campus model object to populate on save.</param>
    /// <param name="isNew">True for new campuses; false when editing.</param>
    /// <param name="onSave">Called with the updated model when the user saves.</param>
    /// <param name="onCancel">Called when the user cancels.</param>
    /// <param name="nameExists">
    /// Predicate returning true when the given name is already taken
    /// (scoped to exclude the current record when editing).
    /// </param>
    public CampusEditViewModel(
        Campus target,
        bool isNew,
        Action<Campus> onSave,
        Action onCancel,
        Func<string, bool> nameExists)
    {
        _target = target;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;
        _nameExists = nameExists;

        Name         = target.Name;
        Abbreviation = target.Abbreviation ?? string.Empty;
    }

    /// <summary>Writes field values back to the model and invokes the save callback.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _target.Name         = Name.Trim();
        _target.Abbreviation = string.IsNullOrWhiteSpace(Abbreviation) ? null : Abbreviation.Trim();
        _onSave(_target);
    }

    /// <summary>Discards changes and invokes the cancel callback.</summary>
    [RelayCommand]
    private void Cancel() => _onCancel();
}
