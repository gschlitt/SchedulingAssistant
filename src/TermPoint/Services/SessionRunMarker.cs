using System.Diagnostics;
using System.Text.Json;

namespace TermPoint.Services;

/// <summary>
/// Notices that the previous TermPoint session on this computer ended without exiting normally (spec item 7, step 7c):
/// a crash, a force-quit of a hung window, a power cut. Each running TermPoint process keeps a small "running" file
/// (its process id, start time and app version) in <see cref="DefaultFolder"/>: written at startup by
/// <see cref="Start"/>, deleted at a normal exit by <see cref="End"/>. A file whose process is no longer running is the
/// trace of a session that never got to <see cref="End"/>.
///
/// <para><b>Why not the dirty marker or a reclaimed lock</b> (the spec's first idea): both also follow clean exits (an
/// exit whose final save failed keeps the marker and parks the lock on purpose; so does a lost lock), so they would
/// report crashes that never happened. The running file says only "this process started and never finished".</para>
///
/// <para><b>Several instances.</b> One file per process, so two TermPoint windows (on different databases) never
/// confuse each other: a file whose process is still running is left alone. A process id the system has since given to
/// another program is told apart by its start time.</para>
///
/// <para><b>Limits.</b> An exit through the exit guard's 2-minute backstop still reaches <see cref="End"/> and counts as
/// normal. Stopping the app from the debugger leaves a file, so a development machine reports it at the next start
/// (release stage "development").</para>
///
/// <para>Never throws: noticing a crash must never cause one. Desktop only.</para>
/// </summary>
public sealed class SessionRunMarker
{
    /// <summary>What a running file records about its process.</summary>
    /// <param name="ProcessId">The process id.</param>
    /// <param name="ProcessStartTimeUtc">When the process started (UTC), to tell a reused process id apart; null if unknown.</param>
    /// <param name="AppVersion">The TermPoint version that was running.</param>
    public sealed record RunInfo(int ProcessId, DateTime? ProcessStartTimeUtc, string AppVersion);

    /// <summary>The folder of the running files: <c>%APPDATA%\TermPoint\running</c>.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TermPoint", "running");

    private readonly string _folder;
    private readonly Func<int, DateTime?, bool> _isRunning;
    private readonly Action<string>? _log;
    private string? _ownFile;

    /// <summary>Creates the marker.</summary>
    /// <param name="folder">Where the running files live; null for <see cref="DefaultFolder"/>.</param>
    /// <param name="isRunning">
    /// Says whether the process with this id and start time is still running; null for the real check. When it can't
    /// tell, it must answer true: a live session is never reported as ended.
    /// </param>
    /// <param name="log">Writes a local log line; null writes nothing.</param>
    public SessionRunMarker(string? folder = null, Func<int, DateTime?, bool>? isRunning = null, Action<string>? log = null)
    {
        _folder    = folder ?? DefaultFolder;
        _isRunning = isRunning ?? IsProcessRunning;
        _log       = log;
    }

    /// <summary>The running file's record for the current process.</summary>
    /// <param name="appVersion">The running TermPoint version.</param>
    /// <returns>This process's id, start time (null if unreadable) and the version.</returns>
    public static RunInfo ForThisProcess(string appVersion)
    {
        DateTime? started = null;
        try
        {
            using var self = Process.GetCurrentProcess();
            started = self.StartTime.ToUniversalTime();
        }
        catch (Exception) { /* start time unknown: a reused id then can't be told apart, which only errs towards "running" */ }
        return new RunInfo(Environment.ProcessId, started, appVersion);
    }

    /// <summary>
    /// Call once at startup. Collects every running file whose process is no longer running (the sessions that ended
    /// unexpectedly), deletes them, then writes this process's own file. An unreadable file is deleted without being
    /// counted (it can't be told apart from a file that was being written). Never throws.
    /// </summary>
    /// <param name="self">This process's record (<see cref="ForThisProcess"/>).</param>
    /// <returns>The records of the sessions that ended unexpectedly, oldest first; empty when there were none.</returns>
    public IReadOnlyList<RunInfo> Start(RunInfo self)
    {
        var ended = new List<RunInfo>();
        try
        {
            Directory.CreateDirectory(_folder);
            foreach (var file in Directory.GetFiles(_folder, "*.run"))
            {
                RunInfo? info = null;
                try { info = JsonSerializer.Deserialize<RunInfo>(File.ReadAllText(file)); }
                catch (Exception) { /* unreadable: deleted below, not counted */ }

                if (info is not null && _isRunning(info.ProcessId, info.ProcessStartTimeUtc))
                    continue;   // another TermPoint that is still running

                if (info is not null)
                    ended.Add(info);
                else
                    _log?.Invoke($"SessionRunMarker: removed an unreadable running file ({Path.GetFileName(file)}).");
                TryDelete(file);
            }

            _ownFile = Path.Combine(_folder, $"{self.ProcessId}.run");
            File.WriteAllText(_ownFile, JsonSerializer.Serialize(self));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SessionRunMarker: could not check or write the running files ({ex.GetType().Name}: {ex.Message}).");
        }

        return ended.OrderBy(i => i.ProcessStartTimeUtc ?? DateTime.MinValue).ToList();
    }

    /// <summary>Call at a normal exit: deletes this process's running file. Safe to call more than once. Never throws.</summary>
    public void End()
    {
        if (_ownFile is not null)
            TryDelete(_ownFile);
        _ownFile = null;
    }

    /// <summary>
    /// The real liveness check: a process with this id exists, has not exited and (when known) started at the recorded
    /// time, within two seconds. "No such process" is false; anything that prevents the check answers true.
    /// </summary>
    /// <param name="processId">The recorded process id.</param>
    /// <param name="startTimeUtc">The recorded start time, or null.</param>
    /// <returns>True when the recorded process is (or may be) still running.</returns>
    private static bool IsProcessRunning(int processId, DateTime? startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;
            if (startTimeUtc is null)
                return true;
            return Math.Abs((process.StartTime.ToUniversalTime() - startTimeUtc.Value).TotalSeconds) < 2;
        }
        catch (ArgumentException)
        {
            return false;   // no process with this id
        }
        catch (Exception)
        {
            return true;    // can't tell: never call a live session ended
        }
    }

    /// <summary>Best-effort delete. Never throws.</summary>
    /// <param name="path">The file to delete.</param>
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { /* left for the next start */ }
    }
}
