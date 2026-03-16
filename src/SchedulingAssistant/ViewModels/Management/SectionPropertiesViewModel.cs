using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionPropertiesViewModel : ViewModelBase
{
    public ObservableCollection<ViewModelBase> Categories { get; }

    [ObservableProperty]
    private ViewModelBase? _selectedCategory;

    public SectionPropertiesViewModel(
        SectionPropertyRepository repo,
        RoomRepository roomRepo,
        SectionRepository sectionRepo,
        InstructorRepository instructorRepo,
        CourseRepository courseRepo,
        DatabaseContext db,
        SectionListViewModel sectionListVm,
        IDialogService dialog)
    {
        Categories = new ObservableCollection<ViewModelBase>
        {
            new SectionPropertyListViewModel(SectionPropertyTypes.SectionType, "Section Type",  repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog),
            new SectionPropertyListViewModel(SectionPropertyTypes.MeetingType, "Meeting Type",  repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog),
            new SectionPropertyListViewModel(SectionPropertyTypes.StaffType,   "Staff Type",    repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog),
            new SectionPropertyListViewModel(SectionPropertyTypes.Campus,      "Campus",        repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog, showAbbreviation: true),
            new SectionPropertyListViewModel(SectionPropertyTypes.Tag,         "Tags",          repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog),
            new SectionPropertyListViewModel(SectionPropertyTypes.Resource,    "Resources",     repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog),
            new SectionPropertyListViewModel(SectionPropertyTypes.Reserve,     "Reserve Codes", repo, sectionRepo, instructorRepo, courseRepo, db, sectionListVm, dialog),
            new RoomListViewModel(roomRepo, sectionRepo, sectionListVm, db, dialog),
        };
        SelectedCategory = Categories[0];
    }
}
