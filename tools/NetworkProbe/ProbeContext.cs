namespace NetworkProbe;

/// <summary>
/// Everything the case runner and the controls share for one run: the options, the network rig, the recording logger,
/// the workspace and the way to talk to the console. Built once by <see cref="ProbeRun"/>.
/// </summary>
internal sealed class ProbeContext
{
    /// <summary>The command line.</summary>
    public required ProbeOptions Options { get; init; }

    /// <summary>Cuts, proves and restores the network (real or simulated).</summary>
    public required INetworkRig Rig { get; init; }

    /// <summary>Records the lines TermPoint logs, so each case can show them.</summary>
    public required RecordingProbeLogger Logger { get; init; }

    /// <summary>The probe's folders and test files.</summary>
    public required ProbeWorkspace Workspace { get; init; }

    /// <summary>Prints a progress line to the console.</summary>
    public required Action<string> Say { get; init; }

    /// <summary>
    /// True when the filter names the server's addresses (not <c>--port-only</c>). If the very first cut is never
    /// confirmed, the address syntax is the likely cause and the run says so (spec section 5.2).
    /// </summary>
    public bool FilterNamesAddresses { get; init; }

    /// <summary>Set once any cut has been confirmed. Until then a failed confirmation aborts the run with advice.</summary>
    public bool CutEverConfirmed { get; set; }

    /// <summary>True for the dry run.</summary>
    public bool Simulated => Rig.IsSimulated;
}
