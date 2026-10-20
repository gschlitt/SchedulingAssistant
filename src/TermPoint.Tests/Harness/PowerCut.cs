namespace TermPoint.Tests.Harness;

/// <summary>
/// Thrown by the harness at a crash point where a computer's power is cut (spec item 9.3, step 9.3c-2a), and again at
/// every later crash point and network operation of that computer's dead process. It is how the harness STOPS the
/// operation: production code may catch it (it catches <see cref="Exception"/> in many places) and carry on, which is
/// why the computer is also marked dead (<see cref="ProcessLife.PowerIsOut"/>) so that whatever the code does next
/// is stopped too, and why everything it did after the cut is thrown away when the frozen disk is put back.
/// </summary>
internal sealed class PowerCutException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="where">The crash point name, or the kind of operation, that was stopped.</param>
    public PowerCutException(string where) : base($"The computer had no power at {where}.") => Where = where;

    /// <summary>The crash point name, or the kind of operation, that was stopped.</summary>
    public string Where { get; }
}

/// <summary>
/// One running TermPoint process of a simulated computer, as far as the power is concerned. A <see cref="Computer"/>
/// makes a new one at every launch. The harness remembers, per logical flow of control, which process it belongs to
/// (<see cref="World"/> keeps it in an <c>AsyncLocal</c>), so a crash-point hit or a network operation can be attributed
/// to the right computer even when the production code hops threads, and so that a background task of a process whose
/// power was cut keeps being stopped after the computer has been restarted.
/// </summary>
internal sealed class ProcessLife
{
    /// <summary>True once the power of this process was cut (volatile: read from any thread).</summary>
    private volatile bool _powerIsOut;

    /// <summary>Creates the process record.</summary>
    /// <param name="owner">The computer the process runs on.</param>
    public ProcessLife(Computer owner) => Owner = owner;

    /// <summary>The computer the process runs on.</summary>
    public Computer Owner { get; }

    /// <summary>True once the power was cut: every later crash-point hit and network operation of the process stops at once.</summary>
    public bool PowerIsOut => _powerIsOut;

    /// <summary>Marks the process dead. It stays dead.</summary>
    public void CutPower() => _powerIsOut = true;
}

/// <summary>
/// The disk of a computer and of the share, copied at the instant of a power cut (see
/// <see cref="World.FreezeDisk"/>). Putting it back (<see cref="World.RestoreFrozenDisk"/>) discards everything the
/// dying process did after the cut.
/// </summary>
internal sealed class FrozenDisk
{
    /// <summary>Creates the record.</summary>
    /// <param name="root">The scratch folder that holds both copies.</param>
    /// <param name="share">The copy of the share folder.</param>
    /// <param name="local">The copy of the computer's local folder (without the instance-guard folder).</param>
    /// <param name="exports">The copy of the folder the generated runs' "Save a Copy" files go to (see <see cref="World.NextSavedCopyPath"/>).</param>
    public FrozenDisk(string root, string share, string local, string exports)
    {
        Root    = root;
        Share   = share;
        Local   = local;
        Exports = exports;
    }

    /// <summary>The copy of the folder "Save a Copy" files go to (it exists only when that folder existed at the cut).</summary>
    public string Exports { get; }

    /// <summary>The scratch folder that holds both copies.</summary>
    public string Root { get; }

    /// <summary>The copy of the share folder.</summary>
    public string Share { get; }

    /// <summary>The copy of the computer's local folder.</summary>
    public string Local { get; }
}

/// <summary>
/// A power cut that is armed for one operation of one computer: "when this computer reaches this crash point for the
/// n-th time, cut its power". Created by <see cref="World.RunWithPowerCutAsync"/>; read by the crash-point hook from any
/// thread.
/// </summary>
internal sealed class ArmedPowerCut
{
    /// <summary>How many times the computer has reached the point so far.</summary>
    private int _reached;

    /// <summary>0 until a thread claims the cut (see <see cref="TryFire"/>).</summary>
    private int _fired;

    /// <summary>Creates the armed cut.</summary>
    /// <param name="computer">The computer whose power is cut.</param>
    /// <param name="point">The crash point (one of <see cref="TermPoint.Services.CrashPoints.All"/>).</param>
    /// <param name="occurrence">Which time the point is reached (1 = the first).</param>
    public ArmedPowerCut(Computer computer, string point, int occurrence)
    {
        Computer   = computer;
        Point      = point;
        Occurrence = occurrence;
    }

    /// <summary>The computer whose power is cut.</summary>
    public Computer Computer { get; }

    /// <summary>The crash point.</summary>
    public string Point { get; }

    /// <summary>Which time the point is reached (1 = the first).</summary>
    public int Occurrence { get; }

    /// <summary>True once the cut has happened.</summary>
    public bool Fired => Volatile.Read(ref _fired) != 0;

    /// <summary>The disk as it was at the instant of the cut; null until it fired (or when freezing it failed).</summary>
    public FrozenDisk? Disk { get; set; }

    /// <summary>Why the disk could not be frozen, if it could not (the run then fails: the cut would not be faithful).</summary>
    public Exception? FreezeFailure { get; set; }

    /// <summary>Set when freezing the disk is over (it worked or it failed).</summary>
    private readonly ManualResetEventSlim _frozen = new(false);

    /// <summary>Called by the thread that cut the power when freezing the disk is over.</summary>
    public void MarkFrozen() => _frozen.Set();

    /// <summary>
    /// Waits until the thread that cut the power has finished freezing the disk (the cut can happen on a background
    /// thread while the operation's own thread has already come back).
    /// </summary>
    /// <param name="timeout">The longest to wait.</param>
    /// <returns>True when freezing is over; false when the wait timed out.</returns>
    public bool WaitUntilFrozen(TimeSpan timeout) => _frozen.Wait(timeout);

    /// <summary>
    /// Counts one more arrival of the computer at the point.
    /// </summary>
    /// <returns>True when this arrival is the occurrence the cut waits for.</returns>
    public bool CountArrival() => Interlocked.Increment(ref _reached) == Occurrence;

    /// <summary>Claims the cut for the calling thread, so that only one thread cuts the power.</summary>
    /// <returns>True for the first caller only.</returns>
    public bool TryFire() => Interlocked.CompareExchange(ref _fired, 1, 0) == 0;
}
