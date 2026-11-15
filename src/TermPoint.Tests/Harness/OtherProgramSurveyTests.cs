using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// A hand-picked survey of what happens when another program (antivirus, backup, cloud sync) has a file on the share open (spec item 8.3): every file
/// that matters (the shared database, the temporary file a save uploads, the lock file) with every sharing mode (<see cref="OtherProgramShare"/>), met by
/// each thing a person does (open, save, autosave, close, a heartbeat, a second computer opening). Each scenario is a short list of event keys replayed
/// with the simplest answers to every dialog (see <see cref="KnownSequences"/>); the run stops at the first breach nothing explains.
///
/// <para>The test never fails on a breach: it WRITES what it saw, so the findings can be read and registered in <see cref="KnownFindings"/>. It fails only
/// when a run hangs or the harness itself breaks. Where the report goes: the test output, and the file named by the environment variable
/// <c>TERMPOINT_SURVEY_FILE</c> when it is set. Runs only with <c>TERMPOINT_HARNESS=1</c>.</para>
/// </summary>
public sealed class OtherProgramSurveyTests
{
    /// <summary>The test output.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class for one test.</summary>
    /// <param name="output">xUnit's output for the test.</param>
    public OtherProgramSurveyTests(ITestOutputHelper output) => _output = output;

    /// <summary>The scenarios: a name and the event keys, in order.</summary>
    private static IEnumerable<(string Name, string[] Keys)> Scenarios()
    {
        // Routes the random walks found (a taken-over writer whose demotion meets a held database), with the other program and without it.
        var takenOver = new[] { "Open(bob)", "AdvanceTime(4m)", "Open(alice)", "DeleteLockFile", "Edit(alice)", "Save(bob)" };
        yield return ("random-walk route: alice's lock is deleted, bob saves, then the database is held, alice wakes and closes",
            takenOver.Concat(new[] { "OtherProgramOpens(database,none)", "Sleep(alice,4m)", "Close(alice)" }).ToArray());
        yield return ("same route, stopped before alice closes (where is alice-1 after her demotion?)",
            takenOver.Concat(new[] { "OtherProgramOpens(database,none)", "Sleep(alice,4m)" }).ToArray());
        yield return ("same route, database held with sharing for reads only",
            takenOver.Concat(new[] { "OtherProgramOpens(database,read)", "Sleep(alice,4m)", "Close(alice)" }).ToArray());
        yield return ("same route, database held with full sharing",
            takenOver.Concat(new[] { "OtherProgramOpens(database,readwritedelete)", "Sleep(alice,4m)", "Close(alice)" }).ToArray());
        yield return ("same route, the other program lets go before alice closes",
            takenOver.Concat(new[] { "OtherProgramOpens(database,none)", "Sleep(alice,4m)", "OtherProgramReleases", "Close(alice)" }).ToArray());
        yield return ("same route WITHOUT the other program",
            takenOver.Concat(new[] { "Sleep(alice,4m)", "Close(alice)" }).ToArray());
        foreach (var share in new[] { "none", "read", "readwritedelete" })
        {
            yield return ($"lockfile/{share}: alice crashes, the lock file is held, alice starts again and edits",
                new[] { "Open(alice)", "Crash(alice)", $"OtherProgramOpens(lockfile,{share})", "Open(alice)", "Edit(alice)", "Save(alice)", "OtherProgramReleases", "Close(alice)" });
        }

        foreach (var share in new[] { "none", "read", "readwritedelete" })
        {
            var db    = $"OtherProgramOpens(database,{share})";
            var lck   = $"OtherProgramOpens(lockfile,{share})";
            var cap   = char.ToUpperInvariant(share[0]) + share[1..];
            var dbA   = share switch { "none" => "DatabaseNone", "read" => "DatabaseRead", _ => "DatabaseReadWriteDelete" };
            var tmpA  = share switch { "none" => "SaveTempNone", "read" => "SaveTempRead", _ => "SaveTempReadWriteDelete" };

            // ── The shared database ───────────────────────────────────────
            yield return ($"database/{share}: bob opens", new[] { db, "Open(bob)", "OtherProgramReleases", "Close(bob)" });
            yield return ($"database/{share}: bob has the database open, then saves an edit", new[] { "Open(bob)", "Edit(bob)", db, "Save(bob)", "OtherProgramReleases", "Save(bob)", "Close(bob)" });
            yield return ($"database/{share}: bob autosaves an edit", new[] { "Open(bob)", "Edit(bob)", db, "AutosaveTick(bob)", "OtherProgramReleases", "Close(bob)" });
            yield return ($"database/{share}: bob closes with an edit", new[] { "Open(bob)", "Edit(bob)", db, "Close(bob)", "OtherProgramReleases", "Open(bob)", "Close(bob)" });
            yield return ($"database/{share}: bob closes with nothing to save", new[] { "Open(bob)", db, "Close(bob)", "OtherProgramReleases" });
            yield return ($"database/{share}: alice opens read-only while bob edits", new[] { "Open(bob)", "Edit(bob)", db, "Open(alice)", "OtherProgramReleases", "Close(alice)", "Save(bob)", "Close(bob)" });
            yield return ($"database/{share}: bob switches away with an edit", new[] { "Open(bob)", "Edit(bob)", db, "SwitchAway(bob)", "OtherProgramReleases", "SwitchBack(bob)" });

            // ── The lock file ─────────────────────────────────────────────
            yield return ($"lockfile/{share}: bob's heartbeat", new[] { "Open(bob)", lck, "HeartbeatTick(bob)", "HeartbeatTick(bob)", "OtherProgramReleases", "Close(bob)" });
            yield return ($"lockfile/{share}: bob closes", new[] { "Open(bob)", lck, "Close(bob)", "OtherProgramReleases", "Open(alice)", "Close(alice)" });
            yield return ($"lockfile/{share}: bob saves an edit", new[] { "Open(bob)", "Edit(bob)", lck, "Save(bob)", "OtherProgramReleases", "Save(bob)", "Close(bob)" });
            yield return ($"lockfile/{share}: alice opens while bob holds the lock", new[] { "Open(bob)", lck, "Open(alice)", "OtherProgramReleases", "Close(alice)", "Close(bob)" });
            yield return ($"lockfile/{share}: bob's computer sleeps 10 minutes", new[] { "Open(bob)", lck, "Sleep(bob,10m)", "OtherProgramReleases", "Close(bob)" });
            yield return ($"lockfile/{share}: bob switches away", new[] { "Open(bob)", lck, "SwitchAway(bob)", "OtherProgramReleases", "SwitchBack(bob)", "Close(bob)" });

            // ── A save is held and the other program opens a file meanwhile ─
            foreach (var point in new[] { "Save.AfterLocalSnapshot", "Save.AfterTmpCopy", "Save.AfterTmpVerify" })
            {
                yield return ($"save held at {point}: database/{share}", new[] { "Open(bob)", "Edit(bob)", $"Pause(Save(bob) at {point}; OtherProgramOpens{dbA})", "OtherProgramReleases", "Save(bob)", "Close(bob)" });
                yield return ($"save held at {point}: savetemp/{share}", new[] { "Open(bob)", "Edit(bob)", $"Pause(Save(bob) at {point}; OtherProgramOpens{tmpA})", "OtherProgramReleases", "Save(bob)", "Close(bob)" });
                yield return ($"save held at {point}: lockfile/{share}", new[] { "Open(bob)", "Edit(bob)", $"Pause(Save(bob) at {point}; OtherProgramOpensLockFile{cap switch { "Readwritedelete" => "ReadWriteDelete", var c => c }})", "OtherProgramReleases", "Save(bob)", "Close(bob)" });
            }
        }
    }

    /// <summary>Replays every scenario and reports what it saw. Fails only when a run hangs.</summary>
    [HarnessFact]
    public async Task Survey_WhatAnotherProgramHoldingAFileDoes()
    {
        var report = new StringBuilder();
        var hung = new List<string>();

        foreach (var (name, keys) in Scenarios())
        {
            var record = new RunRecord();
            foreach (var key in keys) record.Steps.Add(new RecordedStep { EventKey = key });

            var result = await RunExecutor.ExecuteAsync(new ReplaySource(record), new RunOptions
            {
                Title = name,
                ExtraEvents = EventCatalogue.ExtrasFor(keys)
            });

            report.AppendLine($"=== {name}");
            report.AppendLine(result.Violations.Count == 0
                ? "  no breach"
                : string.Join(Environment.NewLine, result.Violations.Select(v => $"  BREACH {v.Invariant}/{v.FindingId}: {v.Reason}")));
            report.AppendLine(result.Story);
            report.AppendLine();
            if (result.Hung) hung.Add(name);
        }

        var text = report.ToString();
        _output.WriteLine(text);
        var file = Environment.GetEnvironmentVariable("TERMPOINT_SURVEY_FILE");
        if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, text);

        Assert.Empty(hung);
    }
}
