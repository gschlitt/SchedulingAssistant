namespace NetworkProbe;

/// <summary>
/// Finds the TermPoint repository root, so the report can go to the repository's git-ignored <c>tmp\netprobe</c>
/// folder by default.
/// </summary>
internal static class RepoLocator
{
    /// <summary>
    /// Walks up from the probe's own folder and from the current folder looking for the repository root (a folder that
    /// holds <c>TermPoint.sln</c> and <c>src\TermPoint</c>).
    /// </summary>
    /// <returns>The root, or null if neither starting point is inside the repository.</returns>
    public static string? FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TermPoint.sln")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src", "TermPoint")))
                    return dir.FullName;
            }
        }
        return null;
    }

    /// <summary>
    /// Decides where the report goes: the <c>--report-dir</c> value, else <c>&lt;repo&gt;\tmp\netprobe</c>, else
    /// <c>.\netprobe-reports</c>.
    /// </summary>
    /// <param name="requested">The <c>--report-dir</c> value, or null.</param>
    /// <returns>The full path of the folder (it may not exist yet).</returns>
    public static string ReportDirectory(string? requested)
    {
        if (requested is not null) return Path.GetFullPath(requested);
        var root = FindRoot();
        return root is not null
            ? Path.Combine(root, "tmp", "netprobe")
            : Path.GetFullPath("netprobe-reports");
    }
}
