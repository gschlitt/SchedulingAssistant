namespace TermPoint.Tests.Harness;

/// <summary>
/// An ordinary event of the catalogue, done with a power cut at a named crash point (spec item 9.3, step 9.3c-2a): "Bob
/// presses Save, and his computer loses power at <c>Save.AfterRename</c>". It is enabled exactly when the event it wraps is.
/// When the event reaches the point (the n-th time), the disk is frozen and the event is stopped; the computer's
/// objects are thrown away and the frozen disk is put back (<see cref="World.RunWithPowerCutAsync"/>). When the event
/// never reaches the point it simply happens, and the story says the power was not cut.
///
/// <para>These events are NOT in <see cref="EventCatalogue.Build"/> (the exhaustive search and the random walks would
/// drown in them); a run that uses them is given them in <see cref="RunOptions.ExtraEvents"/>, built from the keys it
/// replays (<see cref="EventCatalogue.ExtrasFor"/>). Their key reads like the decision list it lives in:
/// <c>PowerCut(Save(bob) at Save.AfterRename)</c>, or <c>... #2)</c> for the second time the point is reached.</para>
/// </summary>
internal sealed class PowerCutEvent : HarnessEvent
{
    /// <summary>What every key of a power-cut event starts with.</summary>
    public const string KeyPrefix = "PowerCut(";

    /// <summary>The event that is done while the power goes.</summary>
    private readonly ComputerEvent _inner;

    /// <summary>Creates the event.</summary>
    /// <param name="inner">The event to do.</param>
    /// <param name="point">The crash point at which the power is cut (one of <see cref="TermPoint.Services.CrashPoints.All"/>).</param>
    /// <param name="occurrence">Which time the point is reached (1 = the first).</param>
    public PowerCutEvent(ComputerEvent inner, string point, int occurrence = 1)
    {
        _inner     = inner;
        Point      = point;
        Occurrence = occurrence;
    }

    /// <summary>The crash point at which the power is cut.</summary>
    public string Point { get; }

    /// <summary>Which time the point is reached (1 = the first).</summary>
    public int Occurrence { get; }

    /// <summary>The wrapped event.</summary>
    public ComputerEvent Inner => _inner;

    /// <inheritdoc/>
    public override string Key => $"{KeyPrefix}{_inner.Key} at {Point}{(Occurrence > 1 ? $" #{Occurrence}" : "")})";

    /// <inheritdoc/>
    public override string Story => $"{_inner.Story}, and {char.ToUpperInvariant(_inner.ComputerName[0])}{_inner.ComputerName[1..]}'s computer loses power at {Point}";

    /// <inheritdoc/>
    public override int Weight => 1;

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => _inner.IsEnabled(ctx);

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
        => await ctx.World.RunWithPowerCutAsync(
            _inner.ComputerOf(ctx), Point, Occurrence, _inner.Story, () => _inner.ExecuteAsync(ctx));

    /// <summary>
    /// Reads a key written by <see cref="Key"/> back into the event.
    /// </summary>
    /// <param name="key">An event key.</param>
    /// <param name="events">The events the wrapped one is found in, by key.</param>
    /// <returns>The event; null when the key is not a power-cut key, or names a wrapped event that does not exist or is not a computer's.</returns>
    public static PowerCutEvent? TryParse(string key, IReadOnlyDictionary<string, HarnessEvent> events)
    {
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || !key.EndsWith(')')) return null;

        var body = key[KeyPrefix.Length..^1];
        var at   = body.LastIndexOf(" at ", StringComparison.Ordinal);
        if (at < 0) return null;

        var innerKey = body[..at];
        var rest     = body[(at + 4)..];
        var occurrence = 1;
        var hash = rest.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
        {
            if (!int.TryParse(rest[(hash + 2)..], out occurrence)) return null;
            rest = rest[..hash];
        }

        return events.TryGetValue(innerKey, out var inner) && inner is ComputerEvent computerEvent
            ? new PowerCutEvent(computerEvent, rest, occurrence)
            : null;
    }
}

/// <summary>
/// Presses Refresh on a read-only view: the checkout service re-copies the shared database over the read-only snapshot.
/// A crash-sweep event only (it is not in <see cref="EventCatalogue.Build"/>; the refresh waits about a second on the
/// real clock, which the search could not afford). Enabled when the computer has a read-only session.
/// </summary>
internal sealed class RefreshEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public RefreshEvent(string name) : base(name, "Refresh", "{0} presses Refresh on the read-only view") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx)
        => On(ctx).HasOpenSession && On(ctx).Checkout.Mode == TermPoint.Services.CheckoutMode.ReadOnly && On(ctx).Checkout.IsReadOnlyMode;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx) => await On(ctx).RefreshAsync();
}

/// <summary>
/// Restores the shared database from a backup file (<c>CheckoutService.RestoreFromBackupAsync</c>). The backup is the
/// snapshot a "backup tool" took earlier in the run when there is one, otherwise a copy of the database as it is now. A
/// crash-sweep event only. Enabled when the computer is the writer of a live session.
/// </summary>
internal sealed class RestoreBackupEvent : ComputerEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="name">The computer's name.</param>
    public RestoreBackupEvent(string name) : base(name, "RestoreBackup", "{0} restores the database from a backup file") { }

    /// <inheritdoc/>
    public override bool IsEnabled(RunContext ctx) => On(ctx).HasLiveSession;

    /// <inheritdoc/>
    public override async Task ExecuteAsync(RunContext ctx)
    {
        var backup = ctx.World.NextBackupPath();
        File.WriteAllBytes(backup, ctx.Snapshot ?? ctx.World.TakeSnapshot());
        await On(ctx).RestoreFromBackupAsync(backup);
    }
}

/// <summary>The events that only the crash sweep uses.</summary>
internal static class SweepEvents
{
    /// <summary>Builds the sweep-only plain events, for every computer of the catalogue.</summary>
    /// <returns>The events, in a fixed order.</returns>
    public static IReadOnlyList<HarnessEvent> Build()
    {
        var events = new List<HarnessEvent>();
        foreach (var name in EventCatalogue.ComputerNames)
        {
            events.Add(new RefreshEvent(name));
            events.Add(new RestoreBackupEvent(name));
        }
        return events;
    }
}
