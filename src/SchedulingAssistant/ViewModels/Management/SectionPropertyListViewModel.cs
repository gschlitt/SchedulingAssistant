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
        _db = db;
        _sectionListVm = sectionListVm;
        _dialog = dialog;
        ShowAbbreviation = showAbbreviation;
        Load();
    }

    public void Load() =>
        Items = new ObservableCollection<SectionPropertyValue>(_repo.GetAll(_type));

    [RelayCommand]
    private void Add()
    {
        var value = new SectionPropertyValue();
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
