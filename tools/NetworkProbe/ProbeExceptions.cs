namespace NetworkProbe;

/// <summary>
/// The command line was wrong (unknown option, missing or out-of-range value, a combination that makes no sense).
/// Caught by <see cref="Program"/>, which prints the message and the usage text and exits with code 64.
/// </summary>
internal sealed class UsageException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong, in plain words.</param>
    public UsageException(string message) : base(message) { }
}

/// <summary>
/// The probe refuses to start because a safety rule is not met (not elevated, TermPoint or clumsy already running,
/// the share is on this computer, no usable clumsy.exe...). Nothing has been cut or written when this is thrown.
/// Caught by <see cref="Program"/>, which prints the message and exits with code 3.
/// </summary>
internal sealed class ProbeRefusalException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the probe will not run and what to do about it, in plain words.</param>
    public ProbeRefusalException(string message) : base(message) { }
}

/// <summary>
/// The run cannot go on once it has started (the share did not come back, the filter does not cut the network...).
/// The run stops, restores the network, cleans up and writes the report with this message at the top.
/// </summary>
internal sealed class ProbeAbortException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the run stopped and what to try next, in plain words.</param>
    public ProbeAbortException(string message) : base(message) { }
}
