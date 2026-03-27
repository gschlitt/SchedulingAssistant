using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SchedulingEnvironmentViewModel : ViewModelBase
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
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.SectionType, "Section Type",  repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.MeetingType, "Meeting Type",  repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.StaffType,   "Staff Type",    repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Tag,         "Tags",          repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Resource,    "Resources",     repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService),
            new SchedulingEnvironmentListViewModel(SchedulingEnvironmentTypes.Reserve,     "Reserve Codes", repo, sectionRepo, courseRepo, instructorRepo, db, sectionListVm, dialog, lockService),
            new RoomListViewModel(roomRepo, campusRepo, sectionRepo, sectionListVm, db, dialog, lockService),
            new CampusListViewModel(campusRepo, dialog, lockService),
        };
        SelectedCategory = Categories[0];
    }
}
