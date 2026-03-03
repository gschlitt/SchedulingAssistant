using Avalonia.Controls;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.ViewModels.Management;
using System;

namespace SchedulingAssistant.Views.Management;

public partial class CopySemesterView : UserControl
{
    public CopySemesterView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is CopySemesterViewModel vm)
        {
            // Wire up navigation back to Academic Years
            var root = VisualRoot as Window;
            if (root?.DataContext is MainWindowViewModel mainVm)
            {
                vm.OnNavigateBackToAcademicYears = () => mainVm.NavigateToAcademicYearsCommand.Execute(null);
            }
        }
    }
}
