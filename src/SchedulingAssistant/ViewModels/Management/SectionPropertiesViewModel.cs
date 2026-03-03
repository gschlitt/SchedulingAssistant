using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
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
        DatabaseContext db,
        SectionListViewModel sectionListVm)
    {
        Categories = new ObservableCollection<ViewModelBase>
        {
            new SectionPropertyListViewModel(SectionPropertyTypes.SectionType, "Section Type",  repo, sectionRepo, instructorRepo, db, sectionListVm),
            new SectionPropertyListViewModel(SectionPropertyTypes.MeetingType, "Meeting Type",  repo, sectionRepo, instructorRepo, db, sectionListVm),
            new SectionPropertyListViewModel(SectionPropertyTypes.StaffType,   "Staff Type",    repo, sectionRepo, instructorRepo, db, sectionListVm),
            new SectionPropertyListViewModel(SectionPropertyTypes.Campus,      "Campus",        repo, sectionRepo, instructorRepo, db, sectionListVm, showAbbreviation: true),
            new SectionPropertyListViewModel(SectionPropertyTypes.Tag,         "Tags",          repo, sectionRepo, instructorRepo, db, sectionListVm),
            new SectionPropertyListViewModel(SectionPropertyTypes.Resource,    "Resources",     repo, sectionRepo, instructorRepo, db, sectionListVm),
            new SectionPropertyListViewModel(SectionPropertyTypes.Reserve,     "Reserve Codes", repo, sectionRepo, instructorRepo, db, sectionListVm),
            new RoomListViewModel(roomRepo, sectionRepo, sectionListVm, db),
        };
        SelectedCategory = Categories[0];
    }
}
