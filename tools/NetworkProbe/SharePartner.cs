using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NetworkProbe;

/// <summary>
/// The partner role of the share-semantics experiment (<c>--share-semantics --role partner</c>), run on the second
/// computer: a small TCP server that does file operations on the shared folder for the runner and reports what each one
/// did and how long it took (on this computer's stopwatch; no clock is ever compared across computers).
///
/// <para><b>Safety:</b> every path in a command is RELATIVE to <c>--root</c>; anything rooted, containing <c>..</c> out of
/// the root, or equal to the root itself is refused. Only FileMode.Open on existing files, small CreateNew/Create writes,
/// File.Delete and File.Move inside the root are possible. One connection is served at a time; when it ends (or breaks)
/// every held handle is closed, so nothing stays locked on the share.</para>
/// </summary>
internal sealed class SharePartner
{
    /// <summary>A command the partner will not do (bad arguments, unknown verb, path outside the root).</summary>
    private sealed class RefusedException : Exception
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Why the partner refused.</param>
        public RefusedException(string message) : base(message) { }
    }

    /// <summary>One file the partner holds open on the runner's behalf, with its optional background reader.</summary>
    private sealed class HeldHandle
    {
        /// <summary>The open file.</summary>
        public required FileStream Stream { get; init; }

        /// <summary>The relative path it was opened with, for the log.</summary>
        public required string RelativePath { get; init; }

        /// <summary>Hash of every byte the reader has read so far (guarded by <see cref="Gate"/>).</summary>
        public IncrementalHash Hasher { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        /// <summary>Protects <see cref="Hasher"/>, <see cref="Bytes"/>, <see cref="Finished"/> and <see cref="Error"/>.</summary>
        public object Gate { get; } = new();

        /// <summary>Bytes read so far.</summary>
        public long Bytes;

        /// <summary>True when the reader reached end of file or failed.</summary>
        public bool Finished;

        /// <summary>The reader's failure as <c>Type 0xHRESULT</c>, or null.</summary>
        public string? Error;

        /// <summary>The background reader, or null before READ.</summary>
        public Task? Reader;

        /// <summary>Cancels the reader.</summary>
        public CancellationTokenSource? Cancel;

        /// <summary>Completed when the reader has read its first chunk (or ended).</summary>
        public TaskCompletionSource FirstChunk { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>The root folder, full path, no trailing separator.</summary>
    private readonly string _root;

    /// <summary>The root plus a trailing separator, for the "is under the root" test.</summary>
    private readonly string _rootWithSeparator;

    /// <summary>Where the command log goes (null for silent, used by the in-process dry run).</summary>
    private readonly Action<string>? _log;

    /// <summary>Held handles by id.</summary>
    private readonly Dictionary<string, HeldHandle> _handles = new();

    /// <summary>Source of handle ids.</summary>
    private int _nextHandle;

    /// <summary>
    /// Creates the partner for a root folder.
    /// </summary>
    /// <param name="root">The folder (UNC or local) all commands are confined to.</param>
    /// <param name="log">Receives one line per command and reply; null for none.</param>
    /// <exception cref="ProbeRefusalException">The root does not exist, or is a whole drive or share rather than a folder.</exception>
    public SharePartner(string root, Action<string>? log)
    {
        var full = Path.GetFullPath(root);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(full))
            throw new ProbeRefusalException($"--root '{root}' does not exist (or this computer cannot reach it).");
        if (Path.GetPathRoot(full) is { } pathRoot &&
            string.Equals(pathRoot.TrimEnd(Path.DirectorySeparatorChar), trimmed, StringComparison.OrdinalIgnoreCase))
            throw new ProbeRefusalException($"--root '{root}' is a whole drive or share. Give a dedicated folder inside it, so the partner can never touch anything else.");
        _root = trimmed;
        _rootWithSeparator = trimmed + Path.DirectorySeparatorChar;
        _log = log;
    }

    /// <summary>The folder every command is confined to.</summary>
    public string Root => _root;

    /// <summary>
    /// Listens and serves connections, one at a time, until cancelled.
    /// </summary>
    /// <param name="bind">The local address to listen on (<see cref="IPAddress.Any"/> for the real run, loopback for the dry run).</param>
    /// <param name="port">The TCP port; 0 lets the system pick one.</param>
    /// <param name="listening">Completed with the actual port once listening (null if not needed).</param>
    /// <param name="ct">Stops the server.</param>
    /// <returns>A task that ends when <paramref name="ct"/> is cancelled.</returns>
    /// <exception cref="SocketException">The port could not be opened (already in use, or not allowed).</exception>
    public async Task RunAsync(IPAddress bind, int port, TaskCompletionSource<int>? listening, CancellationToken ct)
    {
        var listener = new TcpListener(bind, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            listening?.TrySetException(ex);
            throw;
        }
        listening?.TrySetResult(((IPEndPoint)listener.LocalEndpoint).Port);
        Log($"listening on {listener.LocalEndpoint}; root {_root}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                Log($"connection from {client.Client.RemoteEndPoint}");
                try
                {
                    await ServeAsync(client, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    Log($"connection broke ({ex.GetType().Name}); closing held handles");
                }
                finally
                {
                    await CloseAllAsync().ConfigureAwait(false);
                    Log("connection closed; waiting for the next one");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop.
        }
        finally
        {
            listener.Stop();
            await CloseAllAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serves one connection: read a command line, execute, write the reply, until QUIT or the connection ends.
    /// </summary>
    /// <param name="client">The connected runner.</param>
    /// <param name="ct">Stops the server.</param>
    /// <returns>A task that ends with the connection.</returns>
    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, utf8);
        using var writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            Log($"<- {line.Replace(ShareProtocol.Sep, ' ')}");
            var (reply, quit) = await HandleAsync(line, ct).ConfigureAwait(false);
            Log($"-> {Shorten(reply)}");
            await writer.WriteLineAsync(reply.AsMemory(), ct).ConfigureAwait(false);
            if (quit) return;
        }
    }

    /// <summary>
    /// Executes one command line and builds its reply. Never throws for a failed file call: the exception becomes an ERR reply.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="ct">Stops the server.</param>
    /// <returns>The reply line, and whether the connection should end after it.</returns>
    private async Task<(string Reply, bool Quit)> HandleAsync(string line, CancellationToken ct)
    {
        var parts = line.Split(ShareProtocol.Sep);
        var verb = parts[0].Trim().ToUpperInvariant();
        var args = parts.Skip(1).ToArray();
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await ExecuteAsync(verb, args, ct).ConfigureAwait(false);
            return (ShareProtocol.FormatOk(sw.Elapsed.TotalMilliseconds, detail), verb == ShareProtocol.Verbs.Quit);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (ShareProtocol.FormatErr(sw.Elapsed.TotalMilliseconds, ex), false);
        }
    }

    /// <summary>
    /// Does the work of one command.
    /// </summary>
    /// <param name="verb">The upper-cased verb.</param>
    /// <param name="args">The TAB-separated arguments.</param>
    /// <param name="ct">Stops the server.</param>
    /// <returns>The reply's detail text.</returns>
    /// <exception cref="RefusedException">Unknown verb, bad arguments, or a path outside the root.</exception>
    /// <exception cref="IOException">The file call failed (the runner wants to see this one).</exception>
    private async Task<string> ExecuteAsync(string verb, string[] args, CancellationToken ct)
    {
        switch (verb)
        {
            case ShareProtocol.Verbs.Ping:
                return Environment.MachineName;

            case ShareProtocol.Verbs.Info:
                return $"machine={Environment.MachineName}; user={Environment.UserName}; os={Environment.OSVersion}; " +
                       $".NET={Environment.Version}; root={_root}";

            case ShareProtocol.Verbs.SmbConfig:
            {
                var (ok, text) = await SmbClientConfig.ReadAsync().ConfigureAwait(false);
                return ok ? text : throw new InvalidOperationException(text);
            }

            case ShareProtocol.Verbs.Exists:
            {
                var path = Resolve(args, 0);
                return File.Exists(path) ? "file" : Directory.Exists(path) ? "dir" : "none";
            }

            case ShareProtocol.Verbs.Create:
            case ShareProtocol.Verbs.CreateNew:
            {
                var path = Resolve(args, 0);
                var mode = verb == ShareProtocol.Verbs.Create ? FileMode.Create : FileMode.CreateNew;
                // Same shape as TermPoint's lock create: write access, share none, a few bytes, close.
                using (var fs = new FileStream(path, mode, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes($"partner {Environment.MachineName} {DateTime.UtcNow:O}");
                    fs.Write(bytes, 0, bytes.Length);
                }
                return "created";
            }

            case ShareProtocol.Verbs.Delete:
                File.Delete(Resolve(args, 0));
                return "deleted";

            case ShareProtocol.Verbs.ReplaceByRename:
                File.Move(Resolve(args, 0), Resolve(args, 1), overwrite: true);
                return "replaced";

            case ShareProtocol.Verbs.Hold:
                return Hold(args);

            case ShareProtocol.Verbs.Read:
                return await StartReadAsync(args).ConfigureAwait(false);

            case ShareProtocol.Verbs.Hash:
            {
                var h = Handle(args, 0);
                var waitMs = args.Length > 1 && int.TryParse(args[1], out var w) ? w : 60_000;
                if (h.Reader is not null)
                    await Task.WhenAny(h.Reader, Task.Delay(waitMs, ct)).ConfigureAwait(false);
                return Status(h);
            }

            case ShareProtocol.Verbs.StopRead:
            {
                var h = Handle(args, 0);
                await StopReaderAsync(h).ConfigureAwait(false);
                return Status(h);
            }

            case ShareProtocol.Verbs.Close:
            {
                var id = args.Length > 0 ? args[0] : "";
                var h = Handle(args, 0);
                await StopReaderAsync(h).ConfigureAwait(false);
                _handles.Remove(id);
                h.Stream.Dispose();
                return "closed";
            }

            case ShareProtocol.Verbs.Quit:
                return "bye";

            default:
                throw new RefusedException($"Unknown command '{verb}'.");
        }
    }

    /// <summary>
    /// Turns a relative path argument into a full path, refusing anything outside the root.
    /// </summary>
    /// <param name="args">The command's arguments.</param>
    /// <param name="index">Which argument holds the path.</param>
    /// <returns>The full path, strictly inside the root.</returns>
    /// <exception cref="RefusedException">Missing, rooted, or resolving outside (or onto) the root.</exception>
    private string Resolve(string[] args, int index)
    {
        if (args.Length <= index || string.IsNullOrWhiteSpace(args[index]))
            throw new RefusedException("A path argument is missing.");
        var relative = args[index];
        if (Path.IsPathRooted(relative))
            throw new RefusedException($"Refused: '{relative}' is not relative to the partner's root.");
        string full;
        try { full = Path.GetFullPath(Path.Join(_root, relative)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RefusedException($"Refused: '{relative}' is not a usable path ({ex.GetType().Name}).");
        }
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new RefusedException($"Refused: '{relative}' is outside the partner's root.");
        return full;
    }

    /// <summary>Finds a held handle by id.</summary>
    /// <param name="args">The command's arguments.</param>
    /// <param name="index">Which argument holds the id.</param>
    /// <returns>The handle.</returns>
    /// <exception cref="RefusedException">No such handle.</exception>
    private HeldHandle Handle(string[] args, int index)
        => args.Length > index && _handles.TryGetValue(args[index], out var h)
            ? h
            : throw new RefusedException("No such handle.");

    /// <summary>
    /// HOLD: opens an existing file (FileMode.Open only) with the given access and sharing and keeps it open.
    /// </summary>
    /// <param name="args">Path, FileAccess (<c>Read</c> or <c>ReadWrite</c>), FileShare (for example <c>ReadWrite+Delete</c>).</param>
    /// <returns>The new handle's id.</returns>
    /// <exception cref="RefusedException">Bad access or share text.</exception>
    /// <exception cref="IOException">The open failed.</exception>
    private string Hold(string[] args)
    {
        var path = Resolve(args, 0);
        if (args.Length < 3 ||
            !Enum.TryParse<FileAccess>(args[1], true, out var access) || access == FileAccess.Write ||
            !Enum.TryParse<FileShare>(args[2].Replace('+', ','), true, out var share) || (share & FileShare.Inheritable) != 0)
            throw new RefusedException("HOLD needs: path, access (Read|ReadWrite), share (None|Read|Write|ReadWrite|Delete, joined with +).");

        // bufferSize 1 = unbuffered, so every read the reader asks for really goes to the share.
        var stream = new FileStream(path, FileMode.Open, access, share, 1, FileOptions.None);
        var id = $"h{++_nextHandle}";
        _handles[id] = new HeldHandle { Stream = stream, RelativePath = args[0] };
        return id;
    }

    /// <summary>
    /// READ: starts a background task that reads the handle from the start to the end in chunks, pausing between them,
    /// and hashes every byte. Replies once the first chunk is in, so the runner knows the read is under way.
    /// </summary>
    /// <param name="args">Handle id, chunk size in bytes, pause between chunks in ms.</param>
    /// <returns>The reader's status after its first chunk.</returns>
    /// <exception cref="RefusedException">Bad arguments, or the handle is already being read.</exception>
    private async Task<string> StartReadAsync(string[] args)
    {
        var h = Handle(args, 0);
        if (args.Length < 3 || !int.TryParse(args[1], out var chunk) || chunk is < 1 or > 1 << 22 ||
            !int.TryParse(args[2], out var delayMs) || delayMs is < 0 or > 5000)
            throw new RefusedException("READ needs: handle, chunk bytes (1..4194304), delay ms (0..5000).");
        if (h.Reader is not null)
            throw new RefusedException("This handle is already being read.");

        h.Cancel = new CancellationTokenSource();
        var token = h.Cancel.Token;
        h.Reader = Task.Run(() => ReadLoop(h, chunk, delayMs, token));
        await Task.WhenAny(h.FirstChunk.Task, Task.Delay(30_000)).ConfigureAwait(false);
        return Status(h);
    }

    /// <summary>
    /// The background reader: sequential reads through the open handle until end of file, an error, or cancellation.
    /// </summary>
    /// <param name="h">The handle.</param>
    /// <param name="chunk">Bytes per read.</param>
    /// <param name="delayMs">Pause after each read, so the read spans the runner's rename.</param>
    /// <param name="token">Cancels the loop.</param>
    private static void ReadLoop(HeldHandle h, int chunk, int delayMs, CancellationToken token)
    {
        var buffer = new byte[chunk];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var n = h.Stream.Read(buffer, 0, buffer.Length);
                lock (h.Gate)
                {
                    if (n > 0)
                    {
                        h.Hasher.AppendData(buffer, 0, n);
                        h.Bytes += n;
                    }
                    else h.Finished = true;
                }
                h.FirstChunk.TrySetResult();
                if (n == 0) return;
                if (delayMs > 0) token.WaitHandle.WaitOne(delayMs);
            }
        }
        catch (Exception ex)
        {
            // The read failed (for example the handle was invalidated by the server): that IS the result.
            lock (h.Gate)
            {
                h.Error = $"{ex.GetType().Name}:{ShareProtocol.HResultText(ex.HResult)}";
                h.Finished = true;
            }
            h.FirstChunk.TrySetResult();
        }
    }

    /// <summary>Stops a handle's background reader (if any) and waits for it.</summary>
    /// <param name="h">The handle.</param>
    /// <returns>A task that ends when the reader has stopped.</returns>
    private static async Task StopReaderAsync(HeldHandle h)
    {
        h.Cancel?.Cancel();
        if (h.Reader is not null)
        {
            try { await h.Reader.ConfigureAwait(false); }
            catch { /* ReadLoop records its own failure */ }
        }
    }

    /// <summary>Describes a handle's reader: bytes read, SHA-256 of them, whether it finished, and any error.</summary>
    /// <param name="h">The handle.</param>
    /// <returns>For example <c>bytes=8388608 sha256=AB12... finished=True error=-</c>.</returns>
    private static string Status(HeldHandle h)
    {
        lock (h.Gate)
        {
            var hash = h.Reader is null ? "-" : Convert.ToHexString(h.Hasher.GetCurrentHash());
            return $"bytes={h.Bytes} sha256={hash} finished={h.Finished} error={h.Error ?? "-"}";
        }
    }

    /// <summary>Closes every held handle (connection ended); failures are only logged.</summary>
    /// <returns>A task that ends when all are closed.</returns>
    private async Task CloseAllAsync()
    {
        foreach (var (id, h) in _handles.ToList())
        {
            try
            {
                await StopReaderAsync(h).ConfigureAwait(false);
                h.Stream.Dispose();
            }
            catch (Exception ex)
            {
                Log($"could not close {id} ({h.RelativePath}): {ex.GetType().Name}");
            }
            _handles.Remove(id);
        }
    }

    /// <summary>Shortens a reply for the log (long SMBCONFIG text).</summary>
    /// <param name="reply">The reply line.</param>
    /// <returns>At most 160 characters.</returns>
    private static string Shorten(string reply)
    {
        var flat = reply.Replace(ShareProtocol.Sep, ' ');
        return flat.Length <= 160 ? flat : flat[..160] + "...";
    }

    /// <summary>Writes a timestamped line to the log, if there is one.</summary>
    /// <param name="text">The line.</param>
    private void Log(string text) => _log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} {text}");
}
