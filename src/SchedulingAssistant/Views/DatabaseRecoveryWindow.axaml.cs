using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SchedulingAssistant.ViewModels;

namespace SchedulingAssistant.Views;

/// <summary>
/// Shown at startup when the configured database is missing or corrupt.
/// Presents three options: browse for the file, restore from a backup, or
/// launch the setup wizard to start fresh.
/// </summary>
/// <remarks>
/// The view model is created in the constructor and exposed via <see cref="Vm"/>
/// so <see cref="MainWindow"/> can read the <see cref="DatabaseRecoveryViewModel.Outcome"/>
/// after the window closes.
/// </remarks>
public partial class DatabaseRecoveryWindow : Window
{
    /// <summary>
    /// The view model for this window. Read by the caller after the window closes
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

        // Close the window when the VM signals it.
        Vm.CloseRequested += Close;

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
