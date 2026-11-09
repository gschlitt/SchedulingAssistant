using System.Text.RegularExpressions;

namespace TermPoint.Services;

/// <summary>
/// Names for the temporary files placed beside a database on the share (1.2.4 item 4.1).
///
/// <para>Every upload that will be renamed onto a database (a save, a restore, a new database, Save a Copy) goes to a
/// name of its own, <c>&lt;database&gt;.&lt;8 hex&gt;.tmp</c>, fresh for every attempt. Two writers (or two attempts of
/// one writer) therefore never open, overwrite, delete or rename each other's file: a frozen upload that the server
/// keeps open blocks only its own name, and a late delete or rename can only touch the file its own save wrote.</para>
///
/// <para>The same class recognises what such a file looks like afterwards (<see cref="IsLeftoverOf"/>), so the sweep
/// at checkout can remove debris, strictly: only the database's own temporary names, never a look-alike a user made.</para>
/// </summary>
internal static class ShareTempFile
{
    /// <summary>Number of hexadecimal characters in the per-attempt part of the name.</summary>
    private const int HexLength = 8;

    /// <summary>The fixed ending of every temporary name.</summary>
    private const string Suffix = ".tmp";

    /// <summary>Matches exactly the per-attempt part of a name: a dot, eight hex digits (any case), then the suffix.</summary>
    private static readonly Regex AttemptPart =
        new(@"^\.[0-9a-f]{8}\.tmp$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// A fresh temporary path beside <paramref name="targetPath"/>: the target path, a dot, eight lowercase hex
    /// characters taken from a new <see cref="Guid"/>, and <c>.tmp</c> (for example <c>BIOL-TT.db.3f2a9c1e.tmp</c>).
    /// A different value is returned on every call, so call it once per attempt and keep the result.
    /// </summary>
    /// <param name="targetPath">Full path of the file the temporary file will be renamed onto.</param>
    /// <returns>The temporary file's full path.</returns>
    internal static string NewPath(string targetPath)
        => targetPath + "." + Guid.NewGuid().ToString("N").Substring(0, HexLength) + Suffix;

    /// <summary>
    /// Whether a file is a leftover temporary file of the database at <paramref name="targetPath"/>: its name is
    /// exactly <c>&lt;database file name&gt;.tmp</c> (the name 1.2.3 used) or <c>&lt;database file name&gt;.&lt;8 hex&gt;.tmp</c>.
    /// Case-insensitive; only the file names are compared, never the folders; anything else (a
    /// <c>BIOL-TT.db.backup.tmp</c>, seven or nine hex digits, extra text after <c>.tmp</c>) is not a leftover.
    /// </summary>
    /// <param name="targetPath">Full path (or bare name) of the database.</param>
    /// <param name="fileNameOrPath">The candidate: a bare file name or a full path.</param>
    /// <returns>True only for the database's own temporary names.</returns>
    internal static bool IsLeftoverOf(string targetPath, string fileNameOrPath)
    {
        var target    = Path.GetFileName(targetPath);
        var candidate = Path.GetFileName(fileNameOrPath);
        if (target.Length == 0 || candidate.Length <= target.Length)
            return false;
        if (!candidate.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = candidate.Substring(target.Length);
        return rest.Equals(Suffix, StringComparison.OrdinalIgnoreCase) || AttemptPart.IsMatch(rest);
    }
}
