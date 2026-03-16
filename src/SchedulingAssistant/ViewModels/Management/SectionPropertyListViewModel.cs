using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionPropertyListViewModel : ViewModelBase
{
    private readonly SectionPropertyRepository _repo;
    private readonly SectionRepository _sectionRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly CourseRepository _courseRepo;
    private readonly DatabaseContext _db;
    private readonly SectionListViewModel _sectionListVm;
    private readonly IDialogService _dialog;
    private readonly string _type;

    public string DisplayName { get; }

    /// <summary>
    /// When true, the "Section Code Abbreviation" field is shown in the edit form and
    /// as a column in the list. Currently only true for the Campus property type.
    /// </summary>
    public bool ShowAbbreviation { get; }

    [ObservableProperty] private ObservableCollection<SectionPropertyValue> _items = new();
    [ObservableProperty] private SectionPropertyValue? _selectedItem;
    [ObservableProperty] private SectionPropertyEditViewModel? _editVm;

    public SectionPropertyListViewModel(
        string propertyType,
        string displayName,
        SectionPropertyRepository repo,
        SectionRepository sectionRepo,
        InstructorRepository instructorRepo,
        CourseRepository courseRepo,
        DatabaseContext db,
        SectionListViewModel sectionListVm,
        IDialogService dialog,
        bool showAbbreviation = false)
    {
        _type = propertyType;
        DisplayName = displayName;
        _repo = repo;
        _sectionRepo = sectionRepo;
        _instructorRepo = instructorRepo;
        _courseRepo = courseRepo;
        _db = db;
        _sectionListVm = sectionListVm;
        _dialog = dialog;
        ShowAbbreviation = showAbbreviation;
        Load();
    }

    public void Load() =>
        Items = new ObservableCollection<SectionPropertyValue>(_repo.GetAll(_type));

    /// <summary>
    /// Re-evaluates the Move Up/Down button states whenever the selection changes.
    /// </summary>
    partial void OnSelectedItemChanged(SectionPropertyValue? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    private bool CanMoveUp()   => SelectedItem != null && Items.IndexOf(SelectedItem) > 0;
    private bool CanMoveDown() => SelectedItem != null && Items.IndexOf(SelectedItem) < Items.Count - 1;

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

    [RelayCommand]
    private void Add()
    {
        // New items land at the end of the list.
        var value = new SectionPropertyValue
        {
            SortOrder = Items.Count > 0 ? Items.Max(i => i.SortOrder) + 1 : 0
        };
        EditVm = new SectionPropertyEditViewModel(value, isNew: true,
            onSave: v => { _repo.Insert(_type, v); Load(); EditVm = null; },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedItem is null) return;
        var copy = new SectionPropertyValue
        {
            Id = SelectedItem.Id,
            Name = SelectedItem.Name,
            SectionCodeAbbreviation = SelectedItem.SectionCodeAbbreviation,
            SortOrder = SelectedItem.SortOrder,
        };
        EditVm = new SectionPropertyEditViewModel(copy, isNew: false,
            onSave: v => { _repo.Update(v); Load(); EditVm = null; _sectionListVm.Reload(); },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(_type, name, excludeId: copy.Id),
            showAbbreviation: ShowAbbreviation);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedItem is null) return;
        var id = SelectedItem.Id;
        var name = SelectedItem.Name;

        var affected = _type == SectionPropertyTypes.StaffType
            ? "all instructors that reference it"
            : _type == SectionPropertyTypes.Tag
                ? "all courses and sections in all semesters that reference it"
                : "all sections in all semesters that reference it";

        if (!await _dialog.Confirm($"Delete \"{name}\"?\n\nThis will also remove it from {affected}."))
            return;

        using var tx = _db.Connection.BeginTransaction();
        try
        {
            // For tags: also remove from all courses that reference this tag.
            if (_type == SectionPropertyTypes.Tag)
            {
                var courses = _courseRepo.GetAll();
                foreach (var course in courses)
                {
                    if (course.TagIds.Remove(id))
                        _courseRepo.Update(course, tx);
                }
            }

            var sections = _sectionRepo.GetAll();
            foreach (var section in sections)
            {
                var changed = false;
                switch (_type)
                {
                    case SectionPropertyTypes.Tag:
                        changed = section.TagIds.Remove(id);
                        break;
                    case SectionPropertyTypes.Resource:
                        changed = section.ResourceIds.Remove(id);
                        break;
                    case SectionPropertyTypes.SectionType:
                        if (section.SectionTypeId == id) { section.SectionTypeId = null; changed = true; }
                        break;
                    case SectionPropertyTypes.Campus:
                        if (section.CampusId == id) { section.CampusId = null; changed = true; }
                        break;
                    case SectionPropertyTypes.Reserve:
                        var before = section.Reserves.Count;
                        section.Reserves.RemoveAll(r => r.ReserveId == id);
                        changed = section.Reserves.Count != before;
                        break;
                    case SectionPropertyTypes.MeetingType:
                        foreach (var sched in section.Schedule.Where(s => s.MeetingTypeId == id))
                        {
                            sched.MeetingTypeId = null;
                            changed = true;
                        }
                        break;
                }
                if (changed) _sectionRepo.Update(section, tx);
            }

            if (_type == SectionPropertyTypes.StaffType)
            {
                var instructors = _instructorRepo.GetAll();
                foreach (var inst in instructors.Where(i => i.StaffTypeId == id))
                {
                    inst.StaffTypeId = null;
                    _instructorRepo.Update(inst, tx);
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
