using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SchedulingEnvironmentListViewModel : ViewModelBase
{
    private readonly ISchedulingEnvironmentRepository _repo;
    private readonly ISectionRepository _sectionRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly IDatabaseContext _db;
    private readonly SectionListViewModel _sectionListVm;
    private readonly IDialogService _dialog;
    private readonly string _type;
    private readonly WriteLockService _lockService;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    public string DisplayName { get; }

    /// <summary>
    /// When true, the "Section Code Abbreviation" field is shown in the edit form and
    /// as a column in the list. Currently only true for the Campus property type.
    /// </summary>
    public bool ShowAbbreviation { get; }

    [ObservableProperty] private ObservableCollection<SchedulingEnvironmentValue> _items = new();
    [ObservableProperty] private SchedulingEnvironmentValue? _selectedItem;
    [ObservableProperty] private SchedulingEnvironmentEditViewModel? _editVm;

    public SchedulingEnvironmentListViewModel(
        string propertyType,
        string displayName,
        ISchedulingEnvironmentRepository repo,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IDatabaseContext db,
        SectionListViewModel sectionListVm,
        IDialogService dialog,
        WriteLockService lockService,
        bool showAbbreviation = false)
    {
        _type = propertyType;
        DisplayName = displayName;
        _repo = repo;
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _db = db;
        _sectionListVm = sectionListVm;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        ShowAbbreviation = showAbbreviation;
        Load();
    }

    public void Load() =>
        Items = new ObservableCollection<SchedulingEnvironmentValue>(_repo.GetAll(_type));

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Re-evaluates the Move Up/Down button states whenever the selection changes.
    /// </summary>
    partial void OnSelectedItemChanged(SchedulingEnvironmentValue? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    private bool CanMoveUp()   => _lockService.IsWriter && SelectedItem != null && Items.IndexOf(SelectedItem) > 0;
    private bool CanMoveDown() => _lockService.IsWriter && SelectedItem != null && Items.IndexOf(SelectedItem) < Items.Count - 1;

    /// <summary>
    /// Moves the selected item one position earlier in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => ApplyMove(Items.IndexOf(SelectedItem!), -1);

    /// <summary>
    /// Moves the selected item one position later in the list and persists the new order.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => ApplyMove(Items.IndexOf(SelectedItem!), +1);

    /// <summary>
    /// Reorders the item at <paramref name="index"/> by <paramref name="delta"/> positions
    /// (+1 or -1), then re-packs all sort orders as 0, 1, 2, … and saves every changed item.
    /// Reloads the list from DB so the UI reflects the stored order.
    /// </summary>
    /// <param name="index">Zero-based index of the item to move.</param>
    /// <param name="delta">Direction: -1 to move up, +1 to move down.</param>
    private void ApplyMove(int index, int delta)
    {
        var list = Items.ToList();
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(index + delta, item);

        // Re-pack sort orders densely (0, 1, 2, …) to avoid gaps accumulating over time.
        for (var i = 0; i < list.Count; i++)
            list[i].SortOrder = i;

        foreach (var v in list)
            _repo.Update(v);

        var selectedId = item.Id;
        Load();
        SelectedItem = Items.FirstOrDefault(v => v.Id == selectedId);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        // New items land at the end of the list.
        var value = new SchedulingEnvironmentValue
        {
            SortOrder = Items.Count > 0 ? Items.Max(i => i.SortOrder) + 1 : 0
        };
        EditVm = new SchedulingEnvironmentEditViewModel(value, isNew: true,
            onSave: v => { _repo.Insert(_type, v); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedItem is null) return;
        var copy = new SchedulingEnvironmentValue
        {
            Id = SelectedItem.Id,
            Name = SelectedItem.Name,
            SectionCodeAbbreviation = SelectedItem.SectionCodeAbbreviation,
            SortOrder = SelectedItem.SortOrder,
        };
        EditVm = new SchedulingEnvironmentEditViewModel(copy, isNew: false,
            onSave: v => { _repo.Update(v); Load(); EditVm = null; _sectionListVm.Reload(); },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name, excludeId: copy.Id),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedItem is null) return;
        var id = SelectedItem.Id;
        var name = SelectedItem.Name;

        var affected = _type == SchedulingEnvironmentTypes.StaffType
            ? "all instructors that reference it"
            : _type == SchedulingEnvironmentTypes.Tag
                ? "all sections and courses that reference it"
                : "all sections in all semesters that reference it";

        if (!await _dialog.Confirm($"Delete \"{name}\"?\n\nThis will also remove it from {affected}."))
            return;

        using var tx = _db.Connection.BeginTransaction();
        try
        {
            var sections = _sectionRepo.GetAll();
            foreach (var section in sections)
            {
                var changed = false;
                switch (_type)
                {
                    case SchedulingEnvironmentTypes.Tag:
                        changed = section.TagIds.Remove(id);
                        break;
                    case SchedulingEnvironmentTypes.Resource:
                        changed = section.ResourceIds.Remove(id);
                        break;
                    case SchedulingEnvironmentTypes.SectionType:
                        if (section.SectionTypeId == id) { section.SectionTypeId = null; changed = true; }
                        break;
                    case SchedulingEnvironmentTypes.Reserve:
                        var before = section.Reserves.Count;
                        section.Reserves.RemoveAll(r => r.ReserveId == id);
                        changed = section.Reserves.Count != before;
                        break;
                    case SchedulingEnvironmentTypes.MeetingType:
                        foreach (var sched in section.Schedule.Where(s => s.MeetingTypeId == id))
                        {
                            sched.MeetingTypeId = null;
                            changed = true;
                        }
                        break;
                }
                if (changed) _sectionRepo.Update(section, tx);
            }

            if (_type == SchedulingEnvironmentTypes.StaffType)
            {
                var instructors = _instructorRepo.GetAll();
                foreach (var inst in instructors.Where(i => i.StaffTypeId == id))
                {
                    inst.StaffTypeId = null;
                    _instructorRepo.Update(inst, tx);
                }
            }

            // Tags: also remove the deleted tag from every course that references it.
            if (_type == SchedulingEnvironmentTypes.Tag)
            {
                var courses = _courseRepo.GetAll();
                foreach (var course in courses)
                {
                    if (course.TagIds.Remove(id))
                        _courseRepo.Update(course, tx);
                }
            }

            _repo.Delete(id, tx);
            tx.Commit();
            Load();
            _sectionListVm.Reload();
        }
        catch (Exception)
        {
            tx.Rollback();
            await _dialog.ShowError("The delete could not be completed. No changes were made. Please try again.");
        }
    }
}
