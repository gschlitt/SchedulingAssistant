using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionListViewModel : ViewModelBase
{
    private readonly SectionRepository _sectionRepo;
    private readonly CourseRepository _courseRepo;
    private readonly InstructorRepository _instructorRepo;
    private readonly RoomRepository _roomRepo;
    private readonly LegalStartTimeRepository _legalStartTimeRepo;
    private readonly SemesterContext _semesterContext;

    [ObservableProperty] private ObservableCollection<SectionListItemViewModel> _sectionItems = new();
    [ObservableProperty] private SectionListItemViewModel? _selectedItem;

    public Section? SelectedSection => SelectedItem?.Section;

    /// <summary>
    /// Wired by SectionListView.axaml.cs to open a floating edit window.
    /// </summary>
    public Action<SectionEditViewModel>? ShowEditWindow { get; set; }

    public SectionListViewModel(
        SectionRepository sectionRepo,
        CourseRepository courseRepo,
        InstructorRepository instructorRepo,
        RoomRepository roomRepo,
        LegalStartTimeRepository legalStartTimeRepo,
        SemesterContext semesterContext)
    {
        _sectionRepo = sectionRepo;
        _courseRepo = courseRepo;
        _instructorRepo = instructorRepo;
        _roomRepo = roomRepo;
        _legalStartTimeRepo = legalStartTimeRepo;
        _semesterContext = semesterContext;

        _semesterContext.PropertyChanged += OnSemesterContextChanged;

        Load();
    }

    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SemesterContext.SelectedSemesterDisplay))
            Load();
    }

    private void Load()
    {
        var semester = _semesterContext.SelectedSemesterDisplay?.Semester;
        if (semester is null) { SectionItems = new(); return; }

        var courseLookup = _courseRepo.GetAll().ToDictionary(c => c.Id);
        var sections = _sectionRepo.GetAll(semester.Id);
        SectionItems = new ObservableCollection<SectionListItemViewModel>(
            sections.Select(s => new SectionListItemViewModel(s, courseLookup)));
    }

    [RelayCommand]
    private void Add()
    {
        var semester = _semesterContext.SelectedSemesterDisplay?.Semester;
        if (semester is null) return;
        var section = new Section { SemesterId = semester.Id };
        OpenEdit(section, isNew: true);
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedSection is null) return;
        OpenEdit(CloneSection(SelectedSection), isNew: false);
    }

    /// <summary>Called from the view when a list item is tapped.</summary>
    public void EditItem(SectionListItemViewModel item)
    {
        SelectedItem = item;
        OpenEdit(CloneSection(item.Section), isNew: false);
    }

    private void OpenEdit(Section section, bool isNew)
    {
        var courses = _courseRepo.GetAllActive();
        var instructors = _instructorRepo.GetAll();
        var rooms = _roomRepo.GetAll();
        var legalStartTimes = _legalStartTimeRepo.GetAll();
        var includeSaturday = AppSettings.Load().IncludeSaturday;
        var editVm = new SectionEditViewModel(section, isNew, courses, instructors, rooms, legalStartTimes,
            includeSaturday,
            onSave: s => { if (isNew) _sectionRepo.Insert(s); else _sectionRepo.Update(s); Load(); });
        ShowEditWindow?.Invoke(editVm);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedSection is null) return;
        _sectionRepo.Delete(SelectedSection.Id);
        Load();
    }

    private static Section CloneSection(Section s) => new()
    {
        Id = s.Id, SemesterId = s.SemesterId, CourseId = s.CourseId,
        InstructorId = s.InstructorId, RoomId = s.RoomId,
        SectionCode = s.SectionCode, Notes = s.Notes,
        Schedule = s.Schedule.Select(d => new SectionDaySchedule
            { Day = d.Day, StartMinutes = d.StartMinutes, DurationMinutes = d.DurationMinutes }).ToList()
    };
}
