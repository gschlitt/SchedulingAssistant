using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using TermPoint.ViewModels;

namespace TermPoint.Services;

/// <summary>
/// What the Support panel (Dev Tools → Support) needs from the desktop: a Save dialog for the logs zip, the clipboard
/// and a way to show a saved file in its folder. A narrow seam, so <see cref="SupportViewModel"/> never needs a window
/// and its tests can fake all of it (the real one is <see cref="AvaloniaSupportPlatform"/>). A missing window or a
/// cancelled dialog is reported as null or false, never as an exception.
/// </summary>
public interface ISupportPlatform
{
    /// <summary>Asks the user where to save the logs zip, starting in their Downloads folder.</summary>
    /// <param name="suggestedFileName">The name the dialog offers (see <see cref="SupportLogs.SuggestedZipName"/>).</param>
    /// <returns>The chosen local path; null when the user cancelled or no window is available.</returns>
    Task<string?> PickZipToSaveAsync(string suggestedFileName);

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when the text was handed to the clipboard; false when there is no clipboard.</returns>
    Task<bool> SetClipboardTextAsync(string text);

    /// <summary>Opens the file manager (Explorer, Finder) at <paramref name="filePath"/>'s folder with the file selected.</summary>
    /// <param name="filePath">Full path of a file that exists.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The file manager could not be started.</exception>
    void ShowInFolder(string filePath);
}

/// <summary>
/// The real <see cref="ISupportPlatform"/>, over the main window's storage provider and clipboard. The window comes
/// from <see cref="MainWindowViewModel.MainWindowReference"/>, as the other flyouts' pickers do.
/// </summary>
public sealed class AvaloniaSupportPlatform : ISupportPlatform
{
    private readonly MainWindowViewModel _mainVm;

    /// <summary>Creates the platform over the main window's view model.</summary>
    /// <param name="mainVm">Holds the window reference once the window exists.</param>
    public AvaloniaSupportPlatform(MainWindowViewModel mainVm) => _mainVm = mainVm;

    /// <inheritdoc />
    public async Task<string?> PickZipToSaveAsync(string suggestedFileName)
    {
        var window = _mainVm.MainWindowReference;
        if (window is null) return null;

        // Let the click that started the command finish before the native dialog opens, as the other saves do.
        await Task.Yield();

        IStorageFolder? downloads = null;
        try { downloads = await window.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads); }
        catch (Exception) { /* no Downloads folder: the dialog opens where it likes */ }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save logs",
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = downloads,
            DefaultExtension = "zip",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Zip file (*.zip)") { Patterns = new[] { "*.zip" } },
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
    public void ShowInFolder(string filePath) => PlatformProcess.ShowInFolder(filePath);
}
