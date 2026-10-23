using System.ComponentModel;
using System.Diagnostics;

namespace NetworkProbe;

/// <summary>Which impairment a clumsy instance applies.</summary>
internal enum CutKind
{
    /// <summary>Slows SMB down (both directions) so a long operation stays in progress long enough to be cut.</summary>
    Lag,

    /// <summary>Drops every SMB packet (both directions): from Windows' point of view the server simply stops answering.</summary>
    Drop
}

/// <summary>
/// Starts and stops clumsy (the WinDivert packet tool) on THIS computer.
///
/// <para><b>Safety (spec section 4):</b> clumsy is started with <see cref="Process.Start()"/> and the
/// <see cref="Process"/> is kept; it is stopped by that process alone (its id), never by name. At most one is running
/// at a time (clumsy allows only one instance). It is stopped when the run finishes, on Ctrl+C
/// (<see cref="Console.CancelKeyPress"/>), when the probe itself crashes, and on <see cref="AppDomain.ProcessExit"/>,
/// so the network is never left cut. Every process id started is remembered and printed, so the user can end a
/// stray one from Task Manager if all else fails.</para>
///
/// <para><b>Command line</b> (names taken from clumsy's source, <c>src/main.c</c>, <c>drop.c</c>, <c>lag.c</c>;
/// <b>not yet confirmed by a run on this computer</b>): <c>--filter "&lt;filter&gt;" --drop on --drop-inbound on
/// --drop-outbound on --drop-chance 100</c>, and for lag <c>--lag on --lag-inbound on --lag-outbound on --lag-time
/// &lt;ms&gt;</c>. Giving parameters makes clumsy start filtering on launch.</para>
/// </summary>
internal sealed class ClumsyController : IDisposable
{
    /// <summary>How long a freshly started clumsy must stay alive before it is considered started (it exits at once when it cannot run).</summary>
    private static readonly TimeSpan StartupCheck = TimeSpan.FromMilliseconds(800);

    /// <summary>How long to wait for a killed clumsy to be gone.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(5);

    /// <summary>Guards <see cref="_process"/> (stop can be called from a Ctrl+C handler thread).</summary>
    private readonly object _gate = new();

    /// <summary>Full path of clumsy.exe.</summary>
    private readonly string _clumsyPath;

    /// <summary>The WinDivert filter.</summary>
    private readonly string _filter;

    /// <summary>Lag in milliseconds for <see cref="CutKind.Lag"/>.</summary>
    private readonly int _lagMs;

    /// <summary>The clumsy this object started and has not stopped, or null.</summary>
    private Process? _process;

    /// <summary>Every process id this object has started, in order.</summary>
    private readonly List<int> _startedPids = new();

    /// <summary>Called with a message whenever clumsy starts or stops (the console).</summary>
    private readonly Action<string> _say;

    /// <summary>Creates a controller; nothing is started yet.</summary>
    /// <param name="clumsyPath">Full path of clumsy.exe.</param>
    /// <param name="filter">The WinDivert filter (contains spaces; quoted for you).</param>
    /// <param name="lagMs">Lag for <see cref="CutKind.Lag"/>.</param>
    /// <param name="say">Receives a line of text whenever clumsy starts or stops.</param>
    public ClumsyController(string clumsyPath, string filter, int lagMs, Action<string> say)
    {
        _clumsyPath = clumsyPath;
        _filter = filter;
        _lagMs = lagMs;
        _say = say;
    }

    /// <summary>Every clumsy process id started so far (for the report).</summary>
    public IReadOnlyList<int> StartedPids
    {
        get { lock (_gate) return _startedPids.ToList(); }
    }

    /// <summary>Whether a clumsy started by this object is running.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    /// <summary>
    /// Builds clumsy's command-line arguments. One list element per argument; the runtime quotes the filter because
    /// it contains spaces.
    /// </summary>
    /// <param name="filter">The WinDivert filter.</param>
    /// <param name="kind">Which impairment.</param>
    /// <param name="lagMs">Lag in milliseconds (used for <see cref="CutKind.Lag"/>).</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> BuildArguments(string filter, CutKind kind, int lagMs)
    {
        var args = new List<string> { "--filter", filter };
        if (kind == CutKind.Lag)
            args.AddRange(new[] { "--lag", "on", "--lag-inbound", "on", "--lag-outbound", "on", "--lag-time", lagMs.ToString() });
        else
            args.AddRange(new[] { "--drop", "on", "--drop-inbound", "on", "--drop-outbound", "on", "--drop-chance", "100" });
        return args;
    }

    /// <summary>
    /// Starts clumsy with the given impairment. Any clumsy this object started earlier is stopped first, so there is
    /// never more than one.
    /// </summary>
    /// <param name="kind">Which impairment.</param>
    /// <returns>The new clumsy's process id.</returns>
    /// <exception cref="InvalidOperationException">clumsy could not be started, or exited straight away (wrong build, no driver, not elevated).</exception>
    public int Start(CutKind kind)
    {
        Stop();

        var info = new ProcessStartInfo(_clumsyPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_clumsyPath) ?? Environment.CurrentDirectory
        };
        foreach (var argument in BuildArguments(_filter, kind, _lagMs))
            info.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("Windows returned no process for clumsy.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Windows would not start '{_clumsyPath}' ({ex.Message}). This computer needs an x64 build, and the probe must be elevated.", ex);
        }

        lock (_gate)
        {
            _process = process;
            _startedPids.Add(process.Id);
        }
        _say($"clumsy ({kind}) started, pid {process.Id}. If the probe is ever stuck, end that pid in Task Manager.");

        // A clumsy that cannot run (wrong architecture, driver refused, bad option) exits within a moment.
        if (process.WaitForExit(StartupCheck))
        {
            var code = process.ExitCode;
            Stop();
            throw new InvalidOperationException(
                $"clumsy exited at once (exit code {code}). Check the build (x64), that the probe is elevated, and that the clumsy options are accepted by this version.");
        }
        return process.Id;
    }

    /// <summary>
    /// Stops the clumsy this object started, by its process id only. Does nothing when none is running. Safe to call
    /// from any thread, any number of times.
    /// </summary>
    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        if (process is null) return;

        var pid = process.Id;
        try
        {
            if (!process.HasExited)
                process.Kill();
            if (process.WaitForExit(ExitWait))
                _say($"clumsy stopped (pid {pid}).");
            else
                _say($"WARNING: clumsy pid {pid} did not exit within {ExitWait.TotalSeconds:F0} s. END IT IN TASK MANAGER NOW, or the network stays cut.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone, or access denied: nothing more this process can do. Say so if it may still be running.
            _say($"clumsy pid {pid}: {ex.Message}. If it is still running, end it in Task Manager.");
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Makes sure clumsy is stopped however the probe ends: on Ctrl+C, on an unhandled crash and on process exit.
    /// </summary>
    /// <param name="onCancel">Called on Ctrl+C after clumsy is stopped (the run cancels its work and cleans up).</param>
    public void InstallSafetyNets(Action onCancel)
    {
        var cancelled = false;
        Console.CancelKeyPress += (_, e) =>
        {
            Stop();
            // First Ctrl+C: let the run unwind and clean up. A second one means "now": let the runtime end the process.
            e.Cancel = !cancelled;
            cancelled = true;
            onCancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Stop();
    }

    /// <summary>Stops clumsy.</summary>
    public void Dispose() => Stop();
}
