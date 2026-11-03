using TermPoint.Services;

namespace NetworkProbe;

/// <summary>
/// One case of the late-landing experiment (<c>--late-landing</c>): which real <see cref="NetworkFileOps"/> call is made
/// with the cut in place, what the files on the server look like before it, and how to read them again afterwards.
/// </summary>
/// <param name="Name">The case name shown in the report and on the console.</param>
/// <param name="CallText">The call, written out for <c>--list</c> and the report.</param>
/// <param name="Why">One line on why this case exists.</param>
/// <param name="Fixtures">The small text files created in the case's own folder before the cut: name and exact text.</param>
/// <param name="ExpectedState">What <paramref name="Observe"/> returns for the folder as created (the pre-cut state).</param>
/// <param name="Execute">Makes the real call for the case's folder and reports what it returned (run on a dedicated thread).</param>
/// <param name="Observe">
/// Reads the folder's state with plain file calls (never <see cref="NetworkFileOps"/>) and describes it in one short
/// string. Any state other than <paramref name="ExpectedState"/> means the abandoned operation took effect. Throws
/// when the share does not answer (the caller treats that as "could not look this time").
/// </param>
internal sealed record LateLandingCase(
    string Name,
    string CallText,
    string Why,
    IReadOnlyList<(string Name, string Text)> Fixtures,
    string ExpectedState,
    Func<string, Task<OpResult>> Execute,
    Func<string, string> Observe);

/// <summary>
/// The two cases of the late-landing experiment ("E1"). Each calls the REAL <see cref="NetworkFileOps"/> method, so the
/// 5 s race and the abandoned inner operation are exactly TermPoint's.
/// </summary>
internal static class LateLandingCatalog
{
    /// <summary>File name of the delete case's only file.</summary>
    private const string DeleteFileName = "late.txt";

    /// <summary>File names of the move case.</summary>
    private const string MoveSourceName = "source.txt";

    /// <summary>The move case's existing target (the file the source is renamed over).</summary>
    private const string MoveTargetName = "target.txt";

    /// <summary>The cases, in the order they run.</summary>
    public static IReadOnlyList<LateLandingCase> All { get; } = new List<LateLandingCase>
    {
        new("late-delete",
            "DeleteAsync(file)  [as TermPoint's lock release does]",
            "Does a delete that was abandoned at the 5 s deadline still remove the file when the network returns?",
            new[] { (DeleteFileName, "BEFORE") },
            ExpectedState: "file present (BEFORE)",
            Execute: async dir => CaseCatalog.FromBool(await NetworkFileOps.DeleteAsync(Path.Combine(dir, DeleteFileName)).ConfigureAwait(false)),
            Observe: dir => ReadTextOrNull(Path.Combine(dir, DeleteFileName)) is { } text ? $"file present ({text})" : "file missing"),

        new("late-move",
            "MoveAsync(source, target, overwrite: true)  [as the heartbeat's rename onto the lock file does]",
            "Does a rename-over-existing that was abandoned at the 5 s deadline still replace the target when the network returns?",
            new[] { (MoveSourceName, "NEW"), (MoveTargetName, "OLD") },
            ExpectedState: "source present; target OLD",
            Execute: async dir => CaseCatalog.FromBool(await NetworkFileOps.MoveAsync(
                Path.Combine(dir, MoveSourceName), Path.Combine(dir, MoveTargetName), overwrite: true).ConfigureAwait(false)),
            Observe: dir =>
            {
                var source = ReadTextOrNull(Path.Combine(dir, MoveSourceName));
                var target = ReadTextOrNull(Path.Combine(dir, MoveTargetName));
                return $"source {(source is null ? "missing" : "present")}; target {target ?? "missing"}";
            })
    };

    /// <summary>
    /// Reads a small text file with a plain open that asks the server (an open is not answered from SMB's 10 s
    /// attribute cache the way <c>File.Exists</c> can be), allowing the file to be deleted or replaced meanwhile so the
    /// look can never get in the way of the operation being watched.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The file's text, trimmed; null when the file (or its folder) does not exist.</returns>
    /// <exception cref="IOException">The share did not answer, or another error other than "not found".</exception>
    private static string? ReadTextOrNull(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
