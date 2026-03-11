using Avalonia.Markup.Xaml;

namespace SchedulingAssistant.Views.Management;

public partial class SectionPrefixListView : Avalonia.Controls.UserControl
{
    public SectionPrefixListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
