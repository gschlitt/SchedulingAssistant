using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TermPoint.ViewModels;

namespace TermPoint.Views;

/// <summary>
/// Shown at startup when the configured database is missing or corrupt.
/// Presents three options: browse for the file, restore from a backup, or
/// launch the setup wizard to start fresh.
/// </summary>
/// <remarks>
/// The view model is created in the constructor and exposed via <see cref="Vm"/>
/// so <see cref="MainWindow"/> can read the <see cref="DatabaseRecoveryViewModel.Outcome"/>
/// after the window hides or closes.
///
/// On resolution the window HIDES (not Close) to avoid disposing the WinUI composition
/// right after a native file picker — that deadlocks the UI thread against the compositor
/// thread (Avalonia 12 bug). The window is disposed at app shutdown when the compositor is idle.
/// </remarks>
public partial class DatabaseRecoveryWindow : Window
{
    /// <summary>
    /// Raised when the window is ready to hand control back to the caller — either the
    /// VM signalled completion or the user clicked X. The window hides (never closes)
    /// to avoid disposing the WinUI composition, which deadlocks the compositor.
    /// </summary>
    public event EventHandler? RecoveryCompleted;

    /// <summary>
    /// The view model for this window. Read by the caller after the window hides/closes
    /// to determine what action to take.
    /// </summary>
    public DatabaseRecoveryViewModel Vm { get; }

    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML compiler for
    /// design-time instantiation. Forwards to the main constructor with safe defaults.
    /// </summary>
    public DatabaseRecoveryWindow() : this(RecoveryReason.NotFound, null) { }

    /// <summary>
    /// Creates the recovery window and wires file-picker delegates.
    /// </summary>
    /// <param name="reason">Why recovery is needed — drives the intro text.</param>
    /// <param name="lastKnownPath">The database path from settings, shown to the user.</param>
    public DatabaseRecoveryWindow(RecoveryReason reason, string? lastKnownPath)
    {
        InitializeComponent();

        Vm = new DatabaseRecoveryViewModel(reason, lastKnownPath);

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
        // Closing disposes the WinUI composition and deadlocks against the compositor
        // thread right after the file picker; hiding sidesteps that.
        Vm.CloseRequested += () =>
        {
            Hide();
            RecoveryCompleted?.Invoke(this, EventArgs.Empty);
        };

        // Also intercept the X-button: cancel the close, hide, and signal.
        // Vm.Outcome remains RecoveryOutcome.None so the caller knows the user exited.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            RecoveryCompleted?.Invoke(this, EventArgs.Empty);
        };

        DataContext = Vm;
    }

    /// <summary>
    /// Enables dragging the window by its custom header bar.
    /// Required because <see cref="WindowDecorations.BorderOnly"/> hides the OS title bar.
    /// </summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
