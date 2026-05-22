using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SchedulingAssistant.Views;

/// <summary>
/// Splash screen shown as the initial <c>desktop.MainWindow</c>.
/// After a minimum display time, creates the real <see cref="MainWindow"/>,
/// swaps <c>desktop.MainWindow</c>, and closes itself.
/// </summary>
public partial class SplashScreen : Window
{
    private const int MinimumDisplayMs = 2000;

    private MainWindow? _mainWindow;

    public SplashScreen()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Create the real MainWindow (hidden) so we can match its size.
        _mainWindow = new MainWindow();
        _mainWindow.IsVisible = false;

        Width = _mainWindow.Width;
        Height = _mainWindow.Height;

        _ = FinishAsync();
    }

    /// <summary>
    /// Waits the minimum display time, then hands off to the real MainWindow.
    /// </summary>
    private async Task FinishAsync()
    {
        await Task.Delay(MinimumDisplayMs);

        if (_mainWindow is not null
            && Application.Current?.ApplicationLifetime
               is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _mainWindow;
            _mainWindow.Show();
        }

        Close();
    }
}
