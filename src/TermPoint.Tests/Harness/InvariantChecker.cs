namespace TermPoint.Tests.Harness;

/// <summary>
/// The state-based invariants of the multi-computer harness (spec item 9.3, step 9.3b), checked after every funnel
/// step against the <see cref="Ledger"/>:
/// <list type="bullet">
/// <item><description><b>I1, nothing committed disappears:</b> the set of edit ids in the shared database may only grow,
/// except across an explicit reset (<see cref="ResetBaselineAtNextCheck"/>: IT restoring an old file, a successful
/// restore from a backup).</description></item>
/// <item><description><b>I2, nothing applied is lost:</b> every edit any computer applied is in D, a live session, a kept
/// working file WITH its marker, or a saved copy, unless a CONFIRMED discard covers it (or an explicit replacement of D removed it on purpose)
/// (<see cref="World.ConfirmedDiscards"/>). An edit only in a support archive is reported separately.</description></item>
/// <item><description><b>I3, the database is whole:</b> D passes <c>PRAGMA integrity_check</c> (on a copy).</description></item>
/// </list>
/// I4 (who may write) is observed at the rename itself by <see cref="World"/>'s crash-point hook, and I5 (messages) by
/// <see cref="PromiseTracker"/>.
/// </summary>
internal sealed class InvariantChecker
{
    /// <summary>The edit ids D has held so far (the I1 baseline).</summary>
    private HashSet<string> _baseline = new();

    /// <summary>True when the next check must adopt D's current edits as the new baseline instead of comparing.</summary>
    private bool _resetPending;

    /// <summary>
    /// Makes the next check adopt D's edits as the baseline: D legitimately lost edits (an explicit "IT restores an old
    /// file" event, or a successful restore from a backup).
    /// </summary>
    public void ResetBaselineAtNextCheck() => _resetPending = true;

    /// <summary>Runs I3, I1 and I2.</summary>
    /// <param name="world">The world.</param>
    /// <param name="now">The ledger as it is now.</param>
    /// <param name="step">The current step.</param>
    /// <returns>The breaches found (repeats of an earlier one are removed by the caller, by key).</returns>
    public IEnumerable<Violation> Check(World world, Ledger now, int step)
    {
        var found = new List<Violation>();

        // I3: whole.
        var problem = world.IntegrityProblem(world.DatabasePath);
        if (problem is not null)
        {
            found.Add(new Violation
            {
                Step = step, Invariant = "I3", Kind = ViolationKind.IntegrityCheckFailed, Key = $"I3:{problem}",
                Reason = $"the shared database fails its integrity check: {problem}"
            });
        }

        // I1 and I2 need to read D; a database that cannot be read is I3's business, and says "cannot tell" here.
        if (now.Shared is null) return found;

        // I1: nothing committed disappears.
        var current = now.Shared.ToHashSet();
        if (_resetPending)
        {
            _baseline     = current;
            _resetPending = false;
        }
        else
        {
            var missing = _baseline.Where(e => !current.Contains(e)).OrderBy(e => e).ToList();
            if (missing.Count > 0)
            {
                found.Add(new Violation
                {
                    Step = step, Invariant = "I1", Kind = ViolationKind.SharedEditsDisappeared,
                    Key = $"I1:{string.Join(",", missing)}",
                    Reason = $"{string.Join(", ", missing)} was in the shared database and is not any more"
                });
                _baseline = current;
            }
            else
            {
                _baseline.UnionWith(current);
            }
        }

        // I2: nothing applied is lost.
        foreach (var computer in world.Computers)
        {
            foreach (var edit in computer.AppliedEdits)
            {
                if (world.ConfirmedDiscards.Contains(edit) || now.IsSafe(edit)) continue;

                if (now.OnlyInSupportArchive(edit))
                {
                    found.Add(new Violation
                    {
                        Step = step, Invariant = "I2", Kind = ViolationKind.EditOnlyInSupportArchive,
                        Key = $"I2:archive:{edit}", Computer = computer.Name, Edit = edit,
                        Reason = $"{edit} ({computer.DisplayName}'s) survives only in a support archive: the user cannot get it back",
                        ComputerPrompts = world.StepPrompts(computer)
                    });
                }
                else
                {
                    found.Add(new Violation
                    {
                        Step = step, Invariant = "I2", Kind = ViolationKind.EditLost,
                        Key = $"I2:lost:{edit}", Computer = computer.Name, Edit = edit,
                        Reason = $"{edit} ({computer.DisplayName}'s) is no longer anywhere that survives a crash",
                        ComputerPrompts = world.StepPrompts(computer)
                    });
                }
            }
        }

        return found;
    }
}
