using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Invariant I5, "messages tell the truth" (spec item 9.3, step 9.3b). Every message a person sees (a message dialog or
/// a banner) is looked up in the catalogue (<see cref="MessagePromises"/>); the promises it makes are remembered and
/// checked when they become decidable:
/// <list type="bullet">
/// <item><description><b>WillOfferRestore</b> ("TermPoint will offer to restore them the next time..."): decidable at that
/// computer's next edit-access open <i>of the same database</i> (production keeps unsaved changes per database, so
/// opening a different one correctly offers nothing). If Restore is chosen the work must really come back: no "Can't Restore
/// Unsaved Changes", and the promised edits in the shared database or the live session. If no restore is offered
/// at all, the work must be back anyway. If the person answers Discard or the X, the promise is vacuous (the loss
/// is invariant I2's business, and F1's). If the work is nowhere any more, I2 reports that and the promise is
/// closed silently.</description></item>
/// <item><description><b>SavedAs</b> ("Your version was saved as X"): decidable at the end of the step; the file X must hold
/// the unsaved edits the computer had when the step began.</description></item>
/// <item><description><b>DatabaseUnchanged</b> ("The database wasn't changed"): decidable at the end of the step; the shared
/// database's edit set must be the same as before the step.</description></item>
/// <item><description><b>ChangesKept</b> ("Your changes are kept"): decidable at the end of the step; the unsaved edits the
/// computer had when the step began must still be somewhere that survives a crash.</description></item>
/// </list>
/// A message that mentions kept / keep / restore / saved / safe but matches no catalogue entry is itself a violation
/// ("unclassified promise"). Promises still undecided when the walk ends are listed in the story; that is not a failure.
/// </summary>
internal sealed class PromiseTracker
{
    /// <summary>One promise a message made.</summary>
    /// <param name="Kind">What it promises.</param>
    /// <param name="Owner">The computer the person saw it on.</param>
    /// <param name="Title">The message title ("" for a banner).</param>
    /// <param name="Text">The message text.</param>
    /// <param name="PromisedEdits">The unsaved edits the computer held when the step began: what the message talks about.</param>
    /// <param name="FileName">For SavedAs: the file the message names.</param>
    /// <param name="Step">The step in which the message was shown.</param>
    /// <param name="Database">
    /// The database the message is about, normalised the way production names the working copy
    /// (<see cref="CheckoutService.NormalizeSourcePath"/>: full path, lower case); null when the owner's checkout had no
    /// source yet. A "will offer to restore" promise is judged only at an edit-access open of this database.
    /// </param>
    /// <param name="DatabaseName">The database's file name, for the story ("dept.db"); empty when <paramref name="Database"/> is null.</param>
    private sealed record Promise(
        PromiseKind Kind, Computer Owner, string Title, string Text,
        IReadOnlyList<string> PromisedEdits, string? FileName, int Step,
        string? Database, string DatabaseName);

    /// <summary>Guards the lists (messages can be shown from a pool thread).</summary>
    private readonly object _gate = new();

    /// <summary>Promises decidable at the end of the step they were made in.</summary>
    private readonly List<Promise> _thisStep = new();

    /// <summary>"Will offer to restore" promises, waiting for the owner's next edit-access open.</summary>
    private readonly List<Promise> _pendingOffers = new();

    /// <summary>
    /// Looks up a message and remembers the promises it makes.
    /// </summary>
    /// <param name="world">The world (for the ledger before the step and the owner's dialogs).</param>
    /// <param name="owner">The computer that showed the message.</param>
    /// <param name="title">The message title ("" for a banner).</param>
    /// <param name="text">The message text.</param>
    /// <param name="step">The current step.</param>
    /// <returns>The violation when the message is an unclassified promise; null otherwise.</returns>
    public Violation? OnMessage(World world, Computer owner, string title, string text, int step)
    {
        var classified = MessagePromises.Classify(title, text);
        if (classified.IsUnclassifiedPromise)
        {
            return new Violation
            {
                Step      = step,
                Invariant = "I5",
                Kind      = ViolationKind.UnclassifiedPromise,
                Reason    = $"{owner.DisplayName} saw a message that talks about kept / restored / saved work but is not in " +
                            $"the promise catalogue: \"{title}\" — {MessagePromises.FirstSentence(text)}",
                Key       = $"I5:unclassified:{title}|{text}",
                Computer  = owner.Name
            };
        }

        var promised = world.LastLedger.UnsavedEditsOf(owner.Name);
        // Which database the message is about. The owner's checkout still points at it whenever such a message is
        // shown: a switch shows "Changes Not Saved" while it tears the old database down, BEFORE the checkout of the
        // new one starts (CheckoutAsync is what moves SourcePath); a close shows it before the process ends; the
        // lock-lost banner and "Unsaved Changes Kept" come while the session on that database is the current one.
        var (database, databaseName) = DatabaseOf(owner);

        lock (_gate)
        {
            foreach (var kind in classified.Kinds.Where(k => k != PromiseKind.Info))
            {
                var promise = new Promise(kind, owner, title, text, promised, MessagePromises.SavedFileName(text), step,
                                          database, databaseName);
                if (kind != PromiseKind.WillOfferRestore)
                {
                    _thisStep.Add(promise);
                    continue;
                }

                // One claim per person and database: "a restore will be offered at your next open of this database".
                // A single failure can show it twice (the lock-lost banner AND "Changes Not Saved" in the same
                // switch); both messages promise the same thing, so they are one claim, judged once. The first stays;
                // the edits the second talks about are added to it.
                var existing = _pendingOffers.FindIndex(p => p.Owner == owner && p.Database == database);
                if (existing < 0)
                    _pendingOffers.Add(promise);
                else
                    _pendingOffers[existing] = _pendingOffers[existing] with
                    {
                        PromisedEdits = _pendingOffers[existing].PromisedEdits.Union(promised).ToList()
                    };
            }
        }
        return null;
    }

    /// <summary>
    /// The database a computer's checkout points at: the one the message it just showed is about (see
    /// <see cref="OnMessage"/>). Normalised exactly as production names the working copy, so two spellings of one path
    /// (case, relative parts) are the same database here as they are there.
    /// </summary>
    /// <param name="owner">The computer.</param>
    /// <returns>The normalised full path and the file name; (null, "") when the checkout has no source yet.</returns>
    private static (string? Database, string Name) DatabaseOf(Computer owner)
    {
        var source = owner.Checkout.SourcePath;
        return string.IsNullOrEmpty(source)
            ? (null, "")
            : (CheckoutService.NormalizeSourcePath(source), Path.GetFileName(source));
    }

    /// <summary>
    /// Decides every promise that has become decidable at the end of a step.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="before">The ledger at the end of the previous step.</param>
    /// <param name="now">The ledger now.</param>
    /// <param name="opened">The computers that completed an open during this step.</param>
    /// <param name="step">The current step.</param>
    /// <returns>The promises that turned out false.</returns>
    public IEnumerable<Violation> Evaluate(World world, Ledger before, Ledger now, IReadOnlyCollection<Computer> opened, int step)
    {
        var broken = new List<Violation>();
        List<Promise> immediate;
        lock (_gate)
        {
            immediate = _thisStep.ToList();
            _thisStep.Clear();
        }

        foreach (var p in immediate)
        {
            var reason = p.Kind switch
            {
                PromiseKind.SavedAs           => CheckSavedAs(world, p, now),
                PromiseKind.DatabaseUnchanged => CheckDatabaseUnchanged(before, now),
                PromiseKind.ChangesKept       => CheckKept(p, now),
                _                             => null
            };
            if (reason is not null) broken.Add(Broken(world, p, step, reason));
        }

        List<Promise> due;
        lock (_gate)
        {
            // Due: the owner finished an open with write access, and the database now open is the one the promise is
            // about. An open of any other database leaves the promise pending. (A promise made before the checkout had
            // a source cannot be told apart and is judged at any edit-access open, as before.)
            due = _pendingOffers
                .Where(p => opened.Contains(p.Owner)
                            && p.Owner.Checkout.Mode == CheckoutMode.WriteAccess
                            && (p.Database is null || p.Database == DatabaseOf(p.Owner).Database))
                .ToList();
            foreach (var p in due) _pendingOffers.Remove(p);
        }

        foreach (var p in due)
        {
            var reason = CheckOffer(world, p, now);
            if (reason is not null) broken.Add(Broken(world, p, step, reason));
        }

        return broken;
    }

    /// <summary>True when a "will offer to restore" promise made to <paramref name="owner"/> still waits for its next edit-access open.</summary>
    /// <param name="owner">The computer.</param>
    public bool HasPendingOffer(Computer owner)
    {
        lock (_gate) return _pendingOffers.Any(p => p.Owner == owner);
    }

    /// <summary>The databases (normalised, see <see cref="CheckoutService.NormalizeSourcePath"/>) a computer has a "will offer to restore" promise pending for.</summary>
    /// <param name="owner">The computer.</param>
    /// <returns>The normalised paths; an empty string stands for a promise whose database was not known.</returns>
    public IReadOnlyList<string> PendingOfferDatabases(Computer owner)
    {
        lock (_gate) return _pendingOffers.Where(p => p.Owner == owner).Select(p => p.Database ?? "").ToList();
    }

    /// <summary>One line per promise still undecided (for the story's closing notes).</summary>
    /// <returns>The lines; empty when nothing is pending.</returns>
    public IReadOnlyList<string> PendingLines()
    {
        lock (_gate)
        {
            return _pendingOffers
                .Select(p => $"Promise still pending: {p.Owner.DisplayName} was told (step {p.Step}, \"{p.Title}\") that " +
                             "TermPoint will offer to restore their unsaved changes next time they open " +
                             (p.Database is null ? "the database" : p.DatabaseName) +
                             " for editing; they have not done so since.")
                .ToList();
        }
    }

    /// <summary>"Your version was saved as X": the file must exist and hold every promised edit.</summary>
    /// <returns>Why the promise is false, or null when it held.</returns>
    private static string? CheckSavedAs(World world, Promise p, Ledger now)
    {
        var path = world.SavedCopyPaths.FirstOrDefault(
            c => string.Equals(Path.GetFileName(c), p.FileName, StringComparison.OrdinalIgnoreCase));
        if (path is null || !now.SavedCopies.TryGetValue(path, out var edits))
            return $"the message said the version was saved as \"{p.FileName}\" but no such file exists";

        var missing = p.PromisedEdits.Where(e => !edits.Contains(e)).ToList();
        return missing.Count == 0
            ? null
            : $"the message said the version was saved as \"{p.FileName}\" but the file lacks {string.Join(", ", missing)}";
    }

    /// <summary>"The database wasn't changed": D's edit set must be the same before and after the step.</summary>
    /// <returns>Why the promise is false, or null when it held (or cannot be told).</returns>
    private static string? CheckDatabaseUnchanged(Ledger before, Ledger now)
    {
        if (before.Shared is null || now.Shared is null) return null;
        return before.Shared.OrderBy(e => e).SequenceEqual(now.Shared.OrderBy(e => e))
            ? null
            : "the message said the database wasn't changed but its edits are different after the step";
    }

    /// <summary>"Your changes are kept": every promised edit must still be somewhere that survives a crash.</summary>
    /// <returns>Why the promise is false, or null when it held.</returns>
    private static string? CheckKept(Promise p, Ledger now)
    {
        var gone = p.PromisedEdits.Where(e => !now.IsSafe(e)).ToList();
        return gone.Count == 0
            ? null
            : $"the message said the changes are kept but {string.Join(", ", gone)} is nowhere that survives a crash";
    }

    /// <summary>
    /// A "will offer to restore" promise at the owner's next edit-access open (see the class remarks for the rules).
    /// </summary>
    /// <returns>Why the promise is false, or null when it held or is vacuous.</returns>
    private static string? CheckOffer(World world, Promise p, Ledger now)
    {
        // Nothing was at stake, or the work is gone altogether (I2 reports that): nothing for this promise to judge.
        if (p.PromisedEdits.Count == 0 || p.PromisedEdits.All(e => !now.IsSafe(e))) return null;

        var prompts = world.StepPrompts(p.Owner);
        var offer   = prompts.FirstOrDefault(c => c.Kind == PromptKind.Choice && c.Title == HarnessTitles.RestoreUnsavedChanges);
        var cantRestore = prompts.Any(c => c.Kind == PromptKind.Choice && c.Title == HarnessTitles.CantRestore);

        var live = now.Live.TryGetValue(p.Owner.Name, out var liveEdits) ? liveEdits : Array.Empty<string>();
        var notBack = p.PromisedEdits.Where(e => !now.InShared(e) && !live.Contains(e)).ToList();

        if (offer is null)
        {
            return notBack.Count == 0
                ? null
                : $"{p.Owner.DisplayName} was told (step {p.Step}) that a restore would be offered at the next open, but at the next " +
                  $"open for editing no restore was offered and {string.Join(", ", notBack)} is not back";
        }

        if (offer.Answer != ChoiceAnswer.Affirmative) return null;   // Discard / X: the person's answer; I2 judges the loss

        if (cantRestore || notBack.Count > 0)
        {
            return $"{p.Owner.DisplayName} was told (step {p.Step}) that a restore would be offered at the next open; at the next " +
                   "open for editing Restore was chosen but " +
                   (cantRestore ? "\"Can't Restore Unsaved Changes\" followed" : $"{string.Join(", ", notBack)} did not come back");
        }
        return null;
    }

    /// <summary>Builds the violation for a broken promise.</summary>
    /// <param name="world">The world.</param>
    /// <param name="p">The promise.</param>
    /// <param name="step">The current step.</param>
    /// <param name="reason">Why it is false.</param>
    private static Violation Broken(World world, Promise p, int step, string reason) => new()
    {
        Step            = step,
        Invariant       = "I5",
        Kind            = ViolationKind.BrokenPromise,
        Reason          = reason,
        Key             = $"I5:broken:{p.Kind}:{p.Step}:{p.Owner.Name}:{p.Title}",
        Computer        = p.Owner.Name,
        Promise         = p.Kind,
        ComputerPrompts = world.StepPrompts(p.Owner)
    };
}
