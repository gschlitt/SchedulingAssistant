using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionEditViewModel : ViewModelBase
{
    [ObservableProperty] private string? _selectedCourseId;
    [ObservableProperty] private string _sectionCode = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string? _selectedInstructorId;
    [ObservableProperty] private string? _selectedRoomId;
    [ObservableProperty] private ObservableCollection<SectionMeetingViewModel> _meetings = new();
    [ObservableProperty] private ObservableCollection<Course> _courses;
    [ObservableProperty] private ObservableCollection<Instructor> _instructors;
    [ObservableProperty] private ObservableCollection<Room> _rooms;

    public string FormTitle => IsNew ? "Add Section" : "Edit Section";
    public bool IsNew { get; }

    private readonly Section _section;
    private readonly Action<Section> _onSave;
    private readonly IReadOnlyList<LegalStartTime> _legalStartTimes;
    private readonly bool _includeSaturday;

    /// <summary>
    /// Set by the view to close the hosting window when Save or Cancel is invoked.
    /// </summary>
    public Action? RequestClose { get; set; }

    public SectionEditViewModel(
        Section section,
        bool isNew,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Instructor> instructors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<LegalStartTime> legalStartTimes,
        bool includeSaturday,
        Action<Section> onSave)
    {
        _section = section;
        IsNew = isNew;
        _onSave = onSave;
        _legalStartTimes = legalStartTimes;
        _includeSaturday = includeSaturday;

        Courses = new ObservableCollection<Course>(courses);
        Instructors = new ObservableCollection<Instructor>(instructors);
        Rooms = new ObservableCollection<Room>(rooms);

        SelectedCourseId = section.CourseId;
        SelectedInstructorId = section.InstructorId;
        SelectedRoomId = section.RoomId;

        SectionCode = section.SectionCode;
        Notes = section.Notes;

        foreach (var entry in section.Schedule)
            Meetings.Add(new SectionMeetingViewModel(legalStartTimes, includeSaturday, entry));
    }

    [RelayCommand]
    private void AddMeeting()
    {
        Meetings.Add(new SectionMeetingViewModel(_legalStartTimes, _includeSaturday));
    }

    [RelayCommand]
    private void RemoveMeeting(SectionMeetingViewModel meeting)
    {
        Meetings.Remove(meeting);
    }

    [RelayCommand]
    private void Save()
    {
        _section.CourseId = SelectedCourseId;
        _section.SectionCode = SectionCode.Trim();
        _section.Notes = Notes.Trim();
        _section.InstructorId = SelectedInstructorId;
        _section.RoomId = SelectedRoomId;
        _section.Schedule = Meetings
            .Select(m => m.ToSchedule())
            .Where(s => s is not null)
            .Cast<SectionDaySchedule>()
            .ToList();
        _onSave(_section);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
