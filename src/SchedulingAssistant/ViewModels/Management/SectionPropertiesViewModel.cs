using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Data.Repositories;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SectionPropertiesViewModel : ViewModelBase
{
    public ObservableCollection<SectionPropertyListViewModel> Categories { get; }

    [ObservableProperty]
    private SectionPropertyListViewModel? _selectedCategory;

    public SectionPropertiesViewModel(SectionPropertyRepository repo)
    {
        Categories = new ObservableCollection<SectionPropertyListViewModel>
        {
            new(SectionPropertyTypes.SectionType, "Section Type",  repo),
            new(SectionPropertyTypes.MeetingType, "Meeting Type",  repo),
            new(SectionPropertyTypes.StaffType,   "Staff Type",    repo),
            new(SectionPropertyTypes.Campus,      "Campus",        repo),
            new(SectionPropertyTypes.Tag,         "Tags",          repo),
            new(SectionPropertyTypes.Resource,    "Resources",     repo),
            new(SectionPropertyTypes.Reserve,     "Reserve Codes", repo),
        };
        SelectedCategory = Categories[0];
    }
}
