using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SchedulingAssistant.Views;

public enum DatabaseLocationMode
{
    FirstRun,
    NotFound,
    OpenExisting
}

public partial class DatabaseLocationDialog : Window
{
    /// <summary>The chosen full file path after OK. Null means cancelled.</summary>
    public string? ChosenPath { get; private set; }

    private readonly DatabaseLocationMode _mode;
    private string? _chosenFolder;

    public DatabaseLocationDialog() : this(DatabaseLocationMode.FirstRun) { }

    public DatabaseLocationDialog(DatabaseLocationMode mode)
    {
        _mode = mode;
        InitializeComponent();

        if (mode == DatabaseLocationMode.FirstRun)
        {
            HeadingText.Text = "Welcome to Scheduling Assistant";
            BodyText.Text =
                "Give your database a name and choose where to save it. " +
                "You can use a local folder or a shared network drive so colleagues can use the same database.";
            ShowCreateSection();
        }
        else if (mode == DatabaseLocationMode.OpenExisting)
        {
            HeadingText.Text = "Open Database";
            BodyText.Text = "Choose an existing database file to open.";
            OnOpenExistingModeClicked(null, null!);
        }
        else
        {
            HeadingText.Text = "Database not found";
            BodyText.Text =
                "The database file could not be found at the previously saved location.";
            NotFoundModeRow.IsVisible = true;
        }
    }

    // ── Mode selection (NotFound only) ────────────────────────────────────

    private void OnOpenExistingModeClicked(object? sender, RoutedEventArgs e)
    {
        NotFoundModeRow.IsVisible = false;
        BodyText.Text = "Browse to locate the existing database file.";
        OpenSection.IsVisible = true;
    }

    private void OnCreateNewModeClicked(object? sender, RoutedEventArgs e)
    {
        NotFoundModeRow.IsVisible = false;
        BodyText.Text = "Give your database a name and choose a folder to save it in.";
        ShowCreateSection();
    }

    private void ShowCreateSection()
    {
        CreateSection.IsVisible = true;
    }

    // ── Create new: name + folder ─────────────────────────────────────────

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder for the database"
        });
        if (folders.Count == 0) return;
        var folder = folders[0].TryGetLocalPath();
        if (folder is null) return;
        _chosenFolder = folder;
        FolderBox.Text = folder;
        UpdateCreatePreview();
    }

    private void OnNameOrFolderChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        UpdateCreatePreview();
    }

    private void UpdateCreatePreview()
    {
        var name = NameBox.Text?.Trim() ?? string.Empty;

        // Validate: non-empty, no invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        if (name.Length == 0 || name.IndexOfAny(invalid) >= 0)
        {
            NameValidation.Text = name.Length == 0
                ? string.Empty
                : "Name contains invalid characters.";
            NameValidation.IsVisible = name.Length > 0 && name.IndexOfAny(invalid) >= 0;
            PreviewPath.IsVisible = false;
            OkButton.IsEnabled = false;
            return;
        }

        NameValidation.IsVisible = false;

        if (_chosenFolder is null)
        {
            PreviewPath.IsVisible = false;
            OkButton.IsEnabled = false;
            return;
        }

        var fullPath = Path.Combine(_chosenFolder, name + ".db");
        PreviewPath.Text = $"Will save as: {fullPath}";
        PreviewPath.IsVisible = true;
        OkButton.IsEnabled = true;
    }

    // ── Open existing: file picker ────────────────────────────────────────

    private async void OnBrowseFileClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open existing database file",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite Database") { Patterns = new[] { "*.db", "*.sqlite" } },
                new FilePickerFileType("All Files")       { Patterns = new[] { "*" } }
            }
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        ExistingPathBox.Text = path;
        OkButton.IsEnabled = true;
    }

    // ── OK / Cancel ───────────────────────────────────────────────────────

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        if (OpenSection.IsVisible)
        {
            ChosenPath = ExistingPathBox.Text;
        }
        else
        {
            var name = NameBox.Text?.Trim() ?? string.Empty;
            ChosenPath = Path.Combine(_chosenFolder!, name + ".db");
        }
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        ChosenPath = null;
        Close();
    }
}
