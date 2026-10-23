using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace NetworkProbe;

/// <summary>
/// The refusals of spec section 4: conditions under which a real run must not start. Each throws a
/// <see cref="ProbeRefusalException"/> whose message says what is wrong and what to do. Nothing has been cut or
/// written at that point. (The dry run needs none of these.)
/// </summary>
internal static class Preflight
{
    /// <summary>
    /// Runs every check, in the order the user is most likely to fix them.
    /// </summary>
    /// <exception cref="ProbeRefusalException">A check failed.</exception>
    public static void RequireAll()
    {
        RequireElevated();
        RequireTermPointClosed();
        RequireNoClumsyRunning();
    }

    /// <summary>
    /// The probe must run elevated: clumsy needs it, and a non-elevated clumsy relaunches itself elevated, after which
    /// the probe would lose track of the process it must stop.
    /// </summary>
    /// <exception cref="ProbeRefusalException">Not Windows, or not elevated.</exception>
    public static void RequireElevated()
    {
        if (!OperatingSystem.IsWindows())
            throw new ProbeRefusalException("The probe works on Windows only (clumsy and SMB client behaviour are Windows things).");
        if (!IsElevated())
            throw new ProbeRefusalException(
                "The probe must run elevated: start the terminal with \"Run as administrator\" and run it again. (clumsy needs it, and a non-elevated clumsy relaunches itself, which the probe could then not stop.)");
    }

    /// <summary>
    /// TermPoint must be closed: it would see the outage too (its heartbeat, its saves), and the probe's results and
    /// TermPoint's data could both be spoiled.
    /// </summary>
    /// <exception cref="ProbeRefusalException">A TermPoint process is running on this computer.</exception>
    public static void RequireTermPointClosed()
    {
        var running = Process.GetProcessesByName("TermPoint");
        var ids = running.Select(p => p.Id).ToList();
        foreach (var process in running) process.Dispose();
        if (ids.Count > 0)
            throw new ProbeRefusalException(
                $"TermPoint is running on this computer (pid {string.Join(", ", ids)}). Close it first: it would see the outage too. (The probe never closes it for you.)");
    }

    /// <summary>
    /// clumsy allows only one instance, and one the probe did not start is one it must not stop.
    /// </summary>
    /// <exception cref="ProbeRefusalException">A clumsy process is already running.</exception>
    public static void RequireNoClumsyRunning()
    {
        var pids = ClumsyLocator.RunningInstances();
        if (pids.Count > 0)
            throw new ProbeRefusalException(
                $"clumsy is already running (pid {string.Join(", ", pids)}); close clumsy first. clumsy allows only one instance, and the probe will not stop a clumsy it did not start.");
    }

    /// <summary>Whether the current process is elevated (a member of the Administrators role with the full token).</summary>
    /// <returns>True when elevated.</returns>
    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
