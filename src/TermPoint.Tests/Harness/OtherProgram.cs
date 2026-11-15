using Microsoft.Win32.SafeHandles;

namespace TermPoint.Tests.Harness;

/// <summary>Which file on the share another program (antivirus, backup, cloud sync) opens (spec item 8.3).</summary>
internal enum OtherProgramFile
{
    /// <summary>The shared database itself (<c>dept.db</c>).</summary>
    Database,

    /// <summary>The temporary file a save uploads before it renames it onto the database (<c>dept.db.&lt;8 hex&gt;.tmp</c>, a fresh name per save). It exists only part of the time.</summary>
    SaveTemp,

    /// <summary>The lock file (<c>dept.lock</c>). It exists while someone holds the lock.</summary>
    LockFile
}

/// <summary>What the other program lets everyone else do with the file while it has it open (the <see cref="FileShare"/> it opens with).</summary>
internal enum OtherProgramShare
{
    /// <summary><see cref="FileShare.None"/>: nobody else can open it at all (a scanner that locks the file).</summary>
    None,

    /// <summary><see cref="FileShare.Read"/>: others may read it, but not write it, replace it or delete it (a backup tool).</summary>
    Read,

    /// <summary><see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>: others may do anything (a well-behaved scanner). The control case.</summary>
    ReadWriteDelete
}

/// <summary>Words, keys and mappings for <see cref="OtherProgramFile"/> and <see cref="OtherProgramShare"/>.</summary>
internal static class OtherProgramNames
{
    /// <summary>The file as an event key writes it ("database", "savetemp", "lockfile").</summary>
    /// <param name="file">The file.</param>
    public static string KeyOf(OtherProgramFile file) => file.ToString().ToLowerInvariant();

    /// <summary>The share mode as an event key writes it ("none", "read", "readwritedelete").</summary>
    /// <param name="share">The share mode.</param>
    public static string KeyOf(OtherProgramShare share) => share.ToString().ToLowerInvariant();

    /// <summary>The file as the story names it.</summary>
    /// <param name="file">The file.</param>
    public static string Words(OtherProgramFile file) => file switch
    {
        OtherProgramFile.Database => "the shared database file",
        OtherProgramFile.SaveTemp => "the temporary file a save uploads",
        _                         => "the lock file"
    };

    /// <summary>The share mode as the story names it.</summary>
    /// <param name="share">The share mode.</param>
    public static string Words(OtherProgramShare share) => share switch
    {
        OtherProgramShare.None => "locking it against everyone",
        OtherProgramShare.Read => "letting others only read it",
        _                      => "letting others read, write and delete it"
    };

    /// <summary>The operating system's sharing flag for a mode.</summary>
    /// <param name="share">The share mode.</param>
    public static FileShare ToFileShare(OtherProgramShare share) => share switch
    {
        OtherProgramShare.None => FileShare.None,
        OtherProgramShare.Read => FileShare.Read,
        _                      => FileShare.ReadWrite | FileShare.Delete
    };
}

/// <summary>
/// One file that another program has open, with a REAL operating-system handle, so the production code meets the real sharing violation
/// (or the real absence of one). Opened read-only, as a scanner or a backup tool would.
/// </summary>
internal sealed class OtherProgramHandle : IDisposable
{
    /// <summary>The open handle.</summary>
    private readonly FileStream _stream;

    /// <summary>Opens the file.</summary>
    /// <param name="file">Which file it is.</param>
    /// <param name="share">What the program lets others do with it.</param>
    /// <param name="path">Full path of the file.</param>
    /// <exception cref="IOException">The file is not there, or someone has it open without sharing.</exception>
    public OtherProgramHandle(OtherProgramFile file, OtherProgramShare share, string path)
    {
        File  = file;
        Share = share;
        Path  = System.IO.Path.GetFullPath(path);
        _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, OtherProgramNames.ToFileShare(share));
        ObserverFiles.Register(this);
    }

    /// <summary>Which file it is.</summary>
    public OtherProgramFile File { get; }

    /// <summary>What the program lets others do with it.</summary>
    public OtherProgramShare Share { get; }

    /// <summary>Full path of the file.</summary>
    public string Path { get; }

    /// <summary>
    /// A read-only stream over the held handle that does NOT own it: the harness's own observers read the file through it, because a handle opened
    /// without sharing keeps out every other open, the harness's too. Disposing the returned stream leaves the handle open.
    /// </summary>
    /// <returns>A stream positioned at the start of the file.</returns>
    public Stream OpenThrough()
    {
        var through = new FileStream(new SafeFileHandle(_stream.SafeFileHandle.DangerousGetHandle(), ownsHandle: false), FileAccess.Read);
        through.Position = 0;   // the handle may have been read through before: every observer reads from the start
        return through;
    }

    /// <summary>Closes the handle: the program lets go of the file.</summary>
    public void Dispose()
    {
        ObserverFiles.Unregister(this);
        _stream.Dispose();
    }
}

/// <summary>
/// How the harness's own observers (the ledger, the invariants, the lock readers) open a file on the share: normally with full sharing, but through
/// another program's handle when that program has the file open (see <see cref="OtherProgramHandle.OpenThrough"/>). The observers must see the disk
/// as it is, whatever another program does to it; the code under test gets no such help. The registry is keyed by full path, and every World uses
/// its own folders, so worlds running side by side never meet each other's handles.
/// </summary>
internal static class ObserverFiles
{
    /// <summary>The handles that are open now, by full path (case-insensitive).</summary>
    private static readonly Dictionary<string, OtherProgramHandle> Held = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guards <see cref="Held"/>.</summary>
    private static readonly object Gate = new();

    /// <summary>Adds an open handle.</summary>
    /// <param name="handle">The handle.</param>
    internal static void Register(OtherProgramHandle handle)
    {
        lock (Gate) Held[handle.Path] = handle;
    }

    /// <summary>Removes a handle that is being closed.</summary>
    /// <param name="handle">The handle.</param>
    internal static void Unregister(OtherProgramHandle handle)
    {
        lock (Gate)
        {
            if (Held.TryGetValue(handle.Path, out var current) && ReferenceEquals(current, handle)) Held.Remove(handle.Path);
        }
    }

    /// <summary>
    /// Opens a file for reading as an observer: with full sharing; and only when that is refused because another program holds the file without
    /// sharing, through that program's handle. The plain open is tried FIRST on purpose: the name may by now belong to a different file than the one the
    /// handle is on (the held file was deleted and a new one created under the same name), and the observer must see the file that is there.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>A readable stream; dispose it when done.</returns>
    /// <exception cref="FileNotFoundException">The file is not there.</exception>
    public static Stream OpenRead(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException) when (HeldHandleFor(path) is not null)
        {
            return HeldHandleFor(path)!.OpenThrough();
        }
    }

    /// <summary>The handle another program holds on <paramref name="path"/>, or null.</summary>
    /// <param name="path">The file.</param>
    private static OtherProgramHandle? HeldHandleFor(string path)
    {
        OtherProgramHandle? handle;
        lock (Gate) Held.TryGetValue(System.IO.Path.GetFullPath(path), out handle);
        return handle;
    }
}
