using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SchedulingEnvironmentViewModel : ViewModelBase, IDismissableEditor
{
    public ObservableCollection<ViewModelBase> Categories { get; }

    [ObservableProperty]
    private ViewModelBase? _selectedCategory;

    public SchedulingEnvironmentViewModel(
        ISchedulingEnvironmentRepository repo,
        IRoomRepository roomRepo,
        ICampusRepository campusRepo,
        ISectionRepository sectionRepo,
        ICourseRepository courseRepo,
        IInstructorRepository instructorRepo,
        IDatabaseContext db,
        SectionListViewModel sectionListVm,
        IDialogService dialog,
        WriteLockService lockService)
    {
        // Pass lockService to every child VM so their button panels are gated consistently.
        Categories = new ObservableCollection<ViewModelBase>
        {
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.SectionType, "Section Type",  repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService,
                description: "Classify sections by format — e.g. Lecture, Lab, Tutorial, Online. Used for filtering in the schedule grid."),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.MeetingType, "Meeting Type",  repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService,
                description: "Describe how a section meets — e.g. In-Person, Hybrid, Remote. Assigned per meeting day on each section."),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.StaffType,   "Staff Type",    repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService,
                description: "Categorize instructors by employment type or role — e.g. Full-Time, Part-Time, Adjunct, Lab Instructor"),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Tag,         "Tags",          repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService,
                description: "Freeform labels that can be attached to sections and courses for custom grouping and filtering e.g. 'Cohort1' or 'PSYMAJOR'"),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Resource,    "Resources",     repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService,
                description: "Special facilities or equipment a section requires — e.g. Recording Studio, Smart Board."),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Reserve,     "Reserve Codes", repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService,
                description: "Enrollment codes for reserved registration access to specific student groups."),
            new RoomListViewModel(roomRepo, campusRepo, sectionRepo, sectionListVm, db, dialog, lockService),
            new CampusListViewModel(campusRepo, dialog, lockService),
        };
        SelectedCategory = Categories[0];
    }

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        return SelectedCategory is IDismissableEditor editor && editor.DismissActiveEditor();
    }
}
