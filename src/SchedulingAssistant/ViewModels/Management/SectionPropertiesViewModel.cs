using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionPropertiesViewModel : ViewModelBase
{
    public ObservableCollection<ViewModelBase> Categories { get; }

    [ObservableProperty]
    private ViewModelBase? _selectedCategory;

    public SectionPropertiesViewModel(SectionPropertyRepository repo, RoomRepository roomRepo)
    {
        Categories = new ObservableCollection<ViewModelBase>
        {
            new SectionPropertyListViewModel(SectionPropertyTypes.SectionType, "Section Type",  repo),
            new SectionPropertyListViewModel(SectionPropertyTypes.MeetingType, "Meeting Type",  repo),
            new SectionPropertyListViewModel(SectionPropertyTypes.StaffType,   "Staff Type",    repo),
            new SectionPropertyListViewModel(SectionPropertyTypes.Campus,      "Campus",        repo, showAbbreviation: true),
            new SectionPropertyListViewModel(SectionPropertyTypes.Tag,         "Tags",          repo),
            new SectionPropertyListViewModel(SectionPropertyTypes.Resource,    "Resources",     repo),
            new SectionPropertyListViewModel(SectionPropertyTypes.Reserve,     "Reserve Codes", repo),
            new RoomListViewModel(roomRepo),
        };
        SelectedCategory = Categories[0];
    }
}
