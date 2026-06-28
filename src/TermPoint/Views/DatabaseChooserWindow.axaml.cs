using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TermPoint.ViewModels;

namespace TermPoint.Views;

/// <summary>
/// Database chooser shown at startup. In normal mode the user picks from recent
/// databases, browses for a file, creates a new database via the wizard, or restores
/// from a backup. In recovery mode an error banner is shown above the same options.
/// </summary>
/// <remarks>
/// On resolution the window HIDES (not Close) to avoid disposing the WinUI composition
/// right after a native file picker — that deadlocks the UI thread against the compositor
/// thread (Avalonia 12 bug). The window is disposed at app shutdown when the compositor is idle.
/// </remarks>
public partial class DatabaseChooserWindow : Window
{
    /// <summary>
    /// Raised when the window is ready to hand control back to the caller — either the
    /// VM signalled completion or the user clicked X. The window hides (never closes)
    /// to avoid disposing the WinUI composition, which deadlocks the compositor.
    /// </summary>
    public event EventHandler? ChooserCompleted;

    /// <summary>
    /// The view model for this window. Read by the caller after the window hides
    /// to determine what action to take.
    /// </summary>
    public DatabaseChooserViewModel Vm { get; }

    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML compiler for
    /// design-time instantiation. Opens in normal chooser mode with safe defaults.
    /// </summary>
    public DatabaseChooserWindow() : this(ChooserMode.Normal, null, null) { }

    /// <summary>
    /// Creates the chooser window and wires file-picker delegates.
    /// </summary>
    /// <param name="mode">Normal chooser or recovery mode.</param>
    /// <param name="reason">Why recovery is needed (ignored in Normal mode).</param>
    /// <param name="lastKnownPath">The database path from settings, shown in recovery mode.</param>
    public DatabaseChooserWindow(ChooserMode mode, RecoveryReason? reason, string? lastKnownPath)
    {
        InitializeComponent();

        Vm = new DatabaseChooserViewModel(mode, reason, lastKnownPath);

        // Inject OS file-picker delegates so the VM can trigger pickers
        // without holding a reference to the Window.

        Vm.PickDatabaseFileAsync = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Locate your database file",
                FileTypeFilter =
                [
                    new FilePickerFileType("SQLite Database") { Patterns = ["*.db", "*.sqlite"] },
                    new FilePickerFileType("All Files")       { Patterns = ["*"] }
                ]
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        Vm.PickBackupFileAsync = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a backup file to restore from",
                FileTypeFilter =
                [
                    new FilePickerFileType("SQLite Database") { Patterns = ["*.db", "*.sqlite"] },
                    new FilePickerFileType("All Files")       { Patterns = ["*"] }
                ]
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        Vm.PickRestoreFolderAsync = async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose where to restore the database"
            });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        };

        // Hide (not Close) when the VM signals completion, then notify the caller.
        Vm.CloseRequested += () =>
        {
            Hide();
            ChooserCompleted?.Invoke(this, EventArgs.Empty);
        };

        // Also intercept the X-button: cancel the close, hide, and signal.
        // Vm.Outcome remains ChooserOutcome.None so the caller knows the user exited.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            ChooserCompleted?.Invoke(this, EventArgs.Empty);
        };

        DataContext = Vm;
    }

    /// <summary>
    /// Enables dragging the window by its custom header bar.
    /// Required because WindowDecorations.BorderOnly hides the OS title bar.
    /// </summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
