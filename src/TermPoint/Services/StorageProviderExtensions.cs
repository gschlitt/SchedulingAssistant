using Avalonia.Platform.Storage;

namespace TermPoint.Services;

/// <summary>
/// Helpers for driving Avalonia's <see cref="IStorageProvider"/> file/folder pickers
/// safely against network locations.
/// </summary>
public static class StorageProviderExtensions
{
    /// <summary>
    /// Resolves <paramref name="path"/> to an <see cref="IStorageFolder"/> suitable for a
    /// picker's <c>SuggestedStartLocation</c>, but only after a deadline-bounded reachability
    /// check so a dead network share cannot stall the UI thread <i>before</i> the picker opens.
    /// <para>The naive approach — call <see cref="IStorageProvider.TryGetFolderFromPathAsync"/>
    /// directly, optionally guarded by a raw <c>Directory.Exists</c> — blocks the UI thread for
    /// the full SMB redirector timeout when the folder is on an unreachable share: both
    /// <c>Directory.Exists</c> and the shell-resolve inside <c>TryGetFolderFromPathAsync</c>
    /// (<c>SHCreateItemFromParsingName</c>) can hang on a dead UNC path. Gating the resolve
    /// behind <see cref="NetworkFileOps.DirectoryExistsAsync"/> (which runs the probe on a
    /// thread-pool thread under the standard deadline) keeps the pre-open step responsive:
    /// an unreachable folder simply yields <c>null</c>, and the picker opens at its default
    /// location instead of freezing.</para>
    /// </summary>
    /// <param name="provider">The storage provider to resolve against.</param>
    /// <param name="path">Candidate folder path; may be null, blank, local, or a UNC share.</param>
    /// <returns>
    /// The resolved folder, or <c>null</c> when <paramref name="path"/> is blank, unreachable
    /// within the <see cref="NetworkFileOps"/> deadline, or not a real folder.
    /// </returns>
    public static async Task<IStorageFolder?> TryGetReachableStartFolderAsync(
        this IStorageProvider provider, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var (completed, exists) = await NetworkFileOps.DirectoryExistsAsync(path);
        if (!completed || !exists) return null;

        return await provider.TryGetFolderFromPathAsync(path);
    }
}
