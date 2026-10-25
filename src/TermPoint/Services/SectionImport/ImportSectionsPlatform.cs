#if !BROWSER
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using TermPoint.ViewModels;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// What the Import Sections flyout needs from the desktop platform: the two file pickers, the clipboard and
/// the two files that ship inside the app (the template workbook and the instructions text). A narrow seam,
/// so the flyout's view model never needs a window and its tests can fake all of it (the real one is
/// <see cref="AvaloniaImportSectionsPlatform"/>). The pickers and the clipboard report a refusal or a missing
/// window as null or false and never throw for them.
/// </summary>
public interface IImportSectionsPlatform
{
    /// <summary>Asks the user which file to import ("Choose file…").</summary>
    /// <returns>The chosen file's local path; null when the user cancelled or no window is available.</returns>
    Task<string?> PickFileToOpenAsync();

    /// <summary>
    /// Asks the user where to save a file ("Save template…" and "Save instructions…"): the dialog's title, the
    /// name it offers, the default extension and the file-type choice all come from the request.
    /// </summary>
    /// <param name="request">What differs between the saves; <see cref="ImportMessages.TemplateSaveRequest"/> or <see cref="ImportMessages.InstructionsSaveRequest"/>.</param>
    /// <returns>The chosen local path; null when the user cancelled or no window is available.</returns>
    Task<string?> PickFileToSaveAsync(SaveFileRequest request);

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when the text was handed to the clipboard; false when there is no clipboard to give it to.</returns>
    Task<bool> SetClipboardTextAsync(string text);

    /// <summary>Reads the template workbook that ships inside the app.</summary>
    /// <returns>The bytes of the <c>.xlsx</c> file.</returns>
    /// <exception cref="IOException">The asset is missing or cannot be read.</exception>
    byte[] ReadTemplate();

    /// <summary>
    /// Reads the instructions text that ships inside the app (the same file the template workbook's Instructions
    /// sheet is built from). Pass it to <see cref="ImportInstructions.Parse"/>.
    /// </summary>
    /// <returns>The text, decoded as UTF-8.</returns>
    /// <exception cref="IOException">The asset is missing or cannot be read.</exception>
    string ReadInstructions();
}

/// <summary>
/// The real <see cref="IImportSectionsPlatform"/>, over the main window's storage provider and clipboard
/// and Avalonia's asset loader. The window comes from <see cref="MainWindowViewModel.MainWindowReference"/>,
/// as the other flyouts' pickers do.
/// </summary>
public sealed class AvaloniaImportSectionsPlatform : IImportSectionsPlatform
{
    /// <summary>
    /// The address of the template workbook in the app's resources (the <c>Assets/**</c> glob of the project
    /// file). No spaces, so it can be addressed this way; the Save dialog suggests a friendlier name.
    /// </summary>
    public static readonly Uri TemplateAssetUri =
        new("avares://TermPoint/Assets/Templates/TermPointSectionsTemplate.xlsx");

    /// <summary>
    /// The address of the instructions text in the app's resources (the same <c>Assets/**</c> glob). It is the
    /// one source of the Instructions sheet, the flyout's instructions and the saved web page.
    /// </summary>
    public static readonly Uri InstructionsAssetUri =
        new("avares://TermPoint/Assets/Templates/SectionImportInstructions.txt");

    private readonly MainWindowViewModel _mainVm;

    /// <summary>Creates the platform over the main window's view model.</summary>
    /// <param name="mainVm">Holds the window reference once the window exists.</param>
    public AvaloniaImportSectionsPlatform(MainWindowViewModel mainVm) => _mainVm = mainVm;

    /// <inheritdoc />
    public async Task<string?> PickFileToOpenAsync()
    {
        var window = _mainVm.MainWindowReference;
        if (window is null) return null;

        // Let the click that started the command finish before the native dialog opens, as the other imports do.
        await Task.Yield();

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the sections file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel workbook or CSV (*.xlsx, *.csv)") { Patterns = new[] { "*.xlsx", "*.csv" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } },
            },
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickFileToSaveAsync(SaveFileRequest request)
    {
        var window = _mainVm.MainWindowReference;
        if (window is null) return null;

        await Task.Yield();

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = request.DefaultExtension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(request.FileTypeName) { Patterns = new[] { request.Pattern } },
            },
        });

        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<bool> SetClipboardTextAsync(string text)
    {
        var clipboard = _mainVm.MainWindowReference?.Clipboard;
        if (clipboard is null) return false;

        await clipboard.SetTextAsync(text);
        return true;
    }

    /// <inheritdoc />
    public byte[] ReadTemplate()
    {
        using var stream = AssetLoader.Open(TemplateAssetUri);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <inheritdoc />
    public string ReadInstructions()
    {
        using var stream = AssetLoader.Open(InstructionsAssetUri);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
#endif
