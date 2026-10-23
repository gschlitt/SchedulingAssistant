using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace NetworkProbe;

/// <summary>
/// The probe's two folders and the test files in them (spec sections 4 and 5.5).
///
/// <para><b>Safety:</b> the probe writes ONLY inside <see cref="ShareRoot"/> (its own <c>netprobe-&lt;time&gt;</c>
/// subfolder of the folder the user named) and <see cref="LocalRoot"/> (a fresh folder under the system temp folder).
/// It never touches a <c>.db</c>, <c>.lock</c>, <c>.tmp</c> or anything else of TermPoint's. Both folders are deleted
/// at the end; <see cref="DeleteAll"/> says what it could not delete.</para>
///
/// <para>All file work here goes through <see cref="RigIo"/>, so a share that stops answering during set-up
/// raises a timeout instead of hanging the probe.</para>
/// </summary>
internal sealed class ProbeWorkspace
{
    /// <summary>Text written to the small test files: a few hundred bytes.</summary>
    private static readonly string SmallText =
        string.Concat(Enumerable.Repeat("NetworkProbe test file, safe to delete.\r\n", 8));

    /// <summary>The probe's folder on the share (or in the dry-run folder).</summary>
    public string ShareRoot { get; }

    /// <summary>The probe's local temp folder.</summary>
    public string LocalRoot { get; }

    /// <summary>The folder any file logger created in this process writes to (kept out of <c>%AppData%</c>).</summary>
    public string LogDirectory => Path.Combine(LocalRoot, "logs");

    /// <summary>Path of the large random file on the local disk.</summary>
    private string BigLocalPath => Path.Combine(LocalRoot, "big-local.bin");

    /// <summary>Path of the small SQLite database on the local disk (copied to the share per case).</summary>
    private string SmallDbLocalPath => Path.Combine(LocalRoot, "small-local.db");

    /// <summary>Size in MB of the file currently at <see cref="BigLocalPath"/>; 0 when none.</summary>
    private int _bigLocalMb;

    /// <summary>
    /// The dry-run folder, when this workspace had to create it (it did not exist). It is removed again at the end,
    /// if it is empty by then, so a dry run leaves nothing behind. Null when the folder was already there.
    /// </summary>
    private readonly string? _createdParent;

    /// <summary>Creates the object; no folder is created yet (see <see cref="Create"/>).</summary>
    /// <param name="shareRoot">The probe's folder on the share.</param>
    /// <param name="localRoot">The probe's local temp folder.</param>
    /// <param name="createdParent">The dry-run folder if this workspace created it, else null.</param>
    private ProbeWorkspace(string shareRoot, string localRoot, string? createdParent)
    {
        ShareRoot = shareRoot;
        LocalRoot = localRoot;
        _createdParent = createdParent;
    }

    /// <summary>
    /// Creates both folders, named by the current time.
    /// </summary>
    /// <param name="shareParent">The folder the user gave (<c>--share</c>) or the dry-run folder.</param>
    /// <param name="createParentIfMissing">
    /// True for a dry run (a missing local folder is created, and removed again at the end). False for a real run: the
    /// share folder must already exist, so the probe never creates anything on the server outside its own subfolder.
    /// </param>
    /// <returns>The workspace, with both folders existing.</returns>
    /// <exception cref="ProbeRefusalException">The share folder does not exist (real run).</exception>
    /// <exception cref="TimeoutException">The share did not answer.</exception>
    /// <exception cref="IOException">A folder could not be created.</exception>
    public static ProbeWorkspace Create(string shareParent, bool createParentIfMissing)
    {
        string? createdParent = null;
        var parentExists = RigIo.Run(() => Directory.Exists(shareParent), RigIo.Short, "looking for the share folder");
        if (!parentExists)
        {
            if (!createParentIfMissing)
                throw new ProbeRefusalException(
                    $"The folder '{shareParent}' does not exist (or the share does not answer). Create it first, or pick a folder that exists: the probe only ever creates its own netprobe-... subfolder inside it.");
            Directory.CreateDirectory(shareParent);
            createdParent = shareParent;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var shareRoot = Path.Combine(shareParent, $"netprobe-{stamp}");
        var localRoot = Path.Combine(Path.GetTempPath(), $"netprobe-local-{stamp}");

        RigIo.Run(() => Directory.CreateDirectory(shareRoot), RigIo.Short, "creating the probe folder on the share");
        Directory.CreateDirectory(localRoot);
        return new ProbeWorkspace(shareRoot, localRoot, createdParent);
    }

    /// <summary>
    /// Creates the files one case needs, in a folder of its own on the share. Runs while the network is normal.
    /// </summary>
    /// <param name="def">The case.</param>
    /// <param name="caseId">A unique folder name for this run of the case, for example <c>copy-cm-2</c>.</param>
    /// <param name="bigMb">Size of the large file for this run.</param>
    /// <returns>The paths the case works with.</returns>
    /// <exception cref="TimeoutException">The share stopped answering during set-up.</exception>
    public CaseFiles BuildFor(CaseDefinition def, string caseId, int bigMb)
    {
        var dir = Path.Combine(ShareRoot, caseId);
        RigIo.Run(() => Directory.CreateDirectory(dir), RigIo.Short, $"creating the folder for {caseId}");

        var files = new CaseFiles(
            Dir: dir,
            SmallFile: Path.Combine(dir, "small.txt"),
            SecondFile: Path.Combine(dir, "second.txt"),
            BigShareFile: Path.Combine(dir, "big.bin"),
            BigLocalFile: BigLocalPath,
            SmallDb: Path.Combine(dir, "small.db"),
            CopyDest: Path.Combine(dir, "copy.bin"),
            MissingFile: Path.Combine(dir, "no-such-file"));

        if (def.Needs.HasFlag(Fixtures.SmallFile))
            RigIo.Run(() => File.WriteAllText(files.SmallFile, SmallText), RigIo.Short, "writing the small test file");
        if (def.Needs.HasFlag(Fixtures.SecondFile))
            RigIo.Run(() => File.WriteAllText(files.SecondFile, SmallText), RigIo.Short, "writing the second small test file");

        if (def.Needs.HasFlag(Fixtures.BigLocal) || def.Needs.HasFlag(Fixtures.BigOnShare))
            EnsureBigLocal(bigMb);
        if (def.Needs.HasFlag(Fixtures.BigOnShare))
            RigIo.Run(() => File.Copy(BigLocalPath, files.BigShareFile, overwrite: true), RigIo.Upload, $"uploading the {bigMb} MB test file");

        if (def.Needs.HasFlag(Fixtures.SmallDb))
        {
            EnsureSmallDb();
            RigIo.Run(() => File.Copy(SmallDbLocalPath, files.SmallDb, overwrite: true), RigIo.Short, "copying the small database");
        }

        return files;
    }

    /// <summary>
    /// Deletes one case's folder on the share. Best effort: a handle an abandoned call still holds can make this fail,
    /// in which case <see cref="DeleteAll"/> retries at the end.
    /// </summary>
    /// <param name="files">The case's paths.</param>
    /// <returns>True when the folder is gone.</returns>
    public bool DeleteCase(CaseFiles files) => DeleteFolderOnce(files.Dir);

    /// <summary>
    /// Deletes one folder on the share, once, without ever hanging. Best effort.
    /// </summary>
    /// <param name="folder">The folder (a case's folder or the control's).</param>
    /// <returns>True when the delete finished without an error.</returns>
    public bool DeleteFolderOnce(string folder)
        => RigIo.TryRun(() => { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }, RigIo.Short);

    /// <summary>
    /// Deletes both folders (after the network is back). Retries a few times because a handle abandoned during a cut is
    /// only released once the network returns.
    /// </summary>
    /// <returns>One line per problem; empty when both folders are gone.</returns>
    public IReadOnlyList<string> DeleteAll()
    {
        var problems = new List<string>();
        if (!DeleteFolder(ShareRoot))
            problems.Add($"Could not delete the probe's folder on the share: {ShareRoot} (safe to delete by hand).");
        if (!DeleteFolder(LocalRoot))
            problems.Add($"Could not delete the probe's local temp folder: {LocalRoot} (safe to delete by hand).");

        // A dry-run folder the probe itself created goes too, but only if nothing else was put in it meanwhile.
        if (_createdParent is not null)
            RigIo.TryRun(() => { if (Directory.Exists(_createdParent)) Directory.Delete(_createdParent, recursive: false); }, RigIo.Short);
        return problems;
    }

    /// <summary>Deletes a folder, trying up to four times, five seconds apart.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>True when it is gone.</returns>
    private static bool DeleteFolder(string folder)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            RigIo.TryRun(() => { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }, RigIo.Short);
            if (!SafeExists(folder))
                return true;
            if (attempt < 4) Thread.Sleep(TimeSpan.FromSeconds(5));
        }
        return false;
    }

    /// <summary>Whether a folder still exists, without ever hanging.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>True if it exists (or could not be checked).</returns>
    private static bool SafeExists(string folder)
    {
        try { return RigIo.Run(() => Directory.Exists(folder), TimeSpan.FromSeconds(10), "checking a folder"); }
        catch { return true; }
    }

    /// <summary>
    /// Makes sure the local large file exists and is <paramref name="mb"/> megabytes of random bytes. Random, so no
    /// compression or de-duplication on the wire can make the transfer faster than its size suggests.
    /// </summary>
    /// <param name="mb">Wanted size in megabytes.</param>
    private void EnsureBigLocal(int mb)
    {
        if (_bigLocalMb == mb && File.Exists(BigLocalPath)) return;

        var chunk = new byte[1024 * 1024];
        using (var stream = new FileStream(BigLocalPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            for (var i = 0; i < mb; i++)
            {
                RandomNumberGenerator.Fill(chunk);
                stream.Write(chunk, 0, chunk.Length);
            }
        }
        _bigLocalMb = mb;
    }

    /// <summary>
    /// Makes sure the small SQLite database exists locally: one table, a few rows, a clean close (rollback-journal
    /// mode, so it is a single self-contained file that can simply be copied).
    /// </summary>
    private void EnsureSmallDb()
    {
        if (File.Exists(SmallDbLocalPath)) return;

        using (var connection = new SqliteConnection($"Data Source={SmallDbLocalPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE probe (id INTEGER PRIMARY KEY, note TEXT NOT NULL);" +
                "INSERT INTO probe (note) VALUES ('one'), ('two'), ('three'), ('four'), ('five');";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools(); // release the file handle so the file can be copied
    }
}
