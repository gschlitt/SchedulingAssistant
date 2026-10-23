# Network probe: do TermPoint's network calls really give up in time? (spec)

**Status:** spec, 2026-10-22. To be built by a Sonnet session; reviewed by Opus.
**Lives in:** `tools/NetworkProbe/` (new). Not part of `TermPoint.sln`, not built by CI, like `tools/ScrollAnchorProbe`.

## 1. Why

The 1.2.3 field test M1 ("black hole mid-save") tried to cut the network at a chosen step of a real save, from a
second computer, by restarting clumsy at a guessed moment. It mostly missed: a save's steps took anywhere from 0.4 s
to 8.5 s, clumsy needs about a second to switch, and a miss looks exactly like a pass. See
`docs/testing/network-field-tests-1.2.3.md` (M1) and the session of 2026-10-22.

Two different questions were mixed together:

1. **Logic.** When a network step times out, does TermPoint react correctly (banner, stays writer, keeps the work,
   recovers)? Already covered deterministically by the automated harness (`NetworkFaults` timeouts at every step,
   `CrashPoints`, pause sweeps). **Out of scope here.**
2. **Platform.** When the real SMB connection goes dark, does every real network call TermPoint makes hand control
   back within its deadline (`NetworkFileOps.TimeoutMs` = 5 s)? Only real Windows + SMB can answer this.
   **This probe answers question 2.**

**The suspected defect it must confirm or refute (finding "A1").** In `NetworkFileOps.CopyCoreAsync`
(`src/TermPoint/Services/NetworkFileOps.cs`, the chunk loop, `dst!.WriteAsync(...)`), the write is raced against
`Task.Delay(TimeoutMs)` only AFTER `WriteAsync` returns its task. The open, the close and the hash are each run under
`Task.Run`, so a blocking Windows call there cannot escape the deadline; the write is not. If Windows performs that
"asynchronous" write synchronously (Windows can, e.g. for writes that extend a new file, or non-cached writes), a cut
mid-copy blocks the copy until the network returns, with no `made no progress` log line and no banner. Field evidence,
2026-10-22 14:03 and 14:48: the save waited 45 s+ with nothing logged, while TermPoint's own heartbeat (a guarded call)
timed out normally in the same window, and completed seconds after the network came back.

## 2. What it is

A .NET 10 console program, run on the **client** computer (the one TermPoint runs on), **elevated**, with TermPoint
closed. It:

- calls TermPoint's **real** `NetworkFileOps` methods (a `ProjectReference` to `src/TermPoint/TermPoint.csproj`; the
  class is public). Nothing is re-implemented;
- cuts the network itself by starting **clumsy on the same computer** (clumsy's WinDivert driver drops this
  computer's own SMB packets: from Windows' point of view the server simply stops answering, the same black hole as
  clumsy on the server). One computer, one clock, nothing to time by hand;
- proves for every case that the cut was really in effect, and that the operation was really in progress when it
  landed, before it reports a verdict;
- writes a table to the console and a Markdown report.

It also includes a **static gap audit** (section 7): network access on the shared path that bypasses `NetworkFileOps`
cannot be seen by the probe, so it is found by reading the code.

## 3. Command line

```
NetworkProbe --share \\mekler\Users\gregs\temp\handtest [options]
```

| Option | Default | Meaning |
|---|---|---|
| `--share <UNC folder>` | required | A folder on the server. The probe creates and works ONLY in its own subfolder `netprobe-<yyyyMMdd-HHmmss>` there, and deletes it at the end. |
| `--clumsy <path>` | auto-find | clumsy.exe. Auto-find order: a running clumsy is a refusal (see 5.3), then `%USERPROFILE%\Downloads`, Desktop, Documents, `C:\tools`, Program Files (depth 4), keeping only an x64 build (read the PE header machine field, as `tmp/handtest_blackhole.ps1` `Get-PeMachine` does). |
| `--port-only` | off | Filter on port 445 only, instead of port 445 AND the server's addresses (5.2). |
| `--hold <s>` | 40 | How long each cut is held. Must be well above the 5 s deadline, so "gave up in time" and "waited for the network" can't be confused. |
| `--big-mb <n>` | 32 | Size of the large test file used for the long (mid-operation) cases. |
| `--lag-ms <n>` | 150 | Lag used to slow a long operation down before the cut (5.4). |
| `--repeat <n>` | 3 | Repetitions of each mid-operation case (SMB caching makes runs differ; see 6). |
| `--only <names>` | all | Comma-separated case names (section 6) to run. |
| `--report-dir <dir>` | `<repo>\tmp\netprobe` if the repo root is found, else `.\netprobe-reports` | Where the Markdown report goes (`tmp/` is gitignored). |
| `--dry-run <local folder>` | | Plumbing check without clumsy and without a server: runs every case against a LOCAL folder, skips the cut (each case reported `SIMULATED`, never pass/fail). Used by the builder to validate the program; needs no elevation. |
| `--list` | | Print the case matrix and exit. |

## 4. Safety rules (non-negotiable)

- **Only its own files.** Writes only inside its `netprobe-…` subfolder on the share and a local temp folder. Never
  touches any `.db`, `.lock`, `.tmp` or anything else of TermPoint's. Cleans up both folders at the end (after the
  network is back), and says so if it couldn't.
- **TermPoint must be closed.** If a `TermPoint` process is running on this computer, refuse and say why (TermPoint
  would see the outage too).
- **Elevation.** Refuse unless running elevated (clumsy needs it; a non-elevated clumsy relaunches itself elevated and
  the probe would lose track of it).
- **clumsy:** start with `Process.Start` and keep the `Process`; stop **by that process id only**. Never stop processes
  by name. If a clumsy is already running at start, refuse ("close clumsy first") — clumsy allows only one instance.
- **Never leave the network cut.** Stop clumsy in `finally`, on Ctrl+C (`Console.CancelKeyPress`) and on
  `AppDomain.ProcessExit`. Print the clumsy pid at start so the user can end it from Task Manager if all else fails.
- **Never write TermPoint's real log or report to BugSnag.** Before the first `NetworkFileOps` call, do what
  `src/TermPoint.Tests/RecordingAppLogger.cs` `KeepAppLoggerAwayFromRealFilesAndBugsnag` does:
  - read `App.Logger` (this runs `App`'s static initialisers: `WriteLockService`, `DatabaseInstanceGuard`; check them
    for side effects such as creating folders under `%AppData%\TermPoint` and note any in the README);
  - if it is a `FileAppLogger`, set its public `BugsnagClient = null`;
  - set `FileAppLogger.LogDirectoryOverride` (internal; use reflection) to the probe's local temp folder;
  - replace `App.Logger` through its compiler-generated backing field `<Logger>k__BackingField` (as the tests do)
    with the probe's own `IAppLogger` that records every line with a timestamp, so each case can show the log
    lines `NetworkFileOps` wrote ("… made no progress for 5000ms", "… timed out after 5000ms", "abandoned …").
- **No changes under `src/`.** No `InternalsVisibleTo`, no production edits. Reflection is acceptable for the two
  internal members above and must be commented as test-rig plumbing.

## 5. Mechanics

### 5.1 Each case runs in its own cycle

1. **Network up?** `ExistsAsync` on the probe folder completes true, and a TCP connect to every server address on
   port 445 succeeds within 2 s. Otherwise wait (up to 120 s) and then abort the run.
2. **Set up** (network normal): create the files the case needs (5.5).
3. **Cut** (5.2), at the point the case defines (before the call, or mid-operation, 5.4).
4. **Confirm the cut** (5.3). If not confirmed within 10 s: stop clumsy, mark the case `NOT TESTED (cut not
   confirmed)`, continue.
5. **Measure** (5.6) while the cut is held for `--hold` seconds.
6. **Restore:** stop clumsy, then wait until step 1 passes again (SMB can take tens of seconds to reconnect); record
   how long recovery took.
7. **Record** the result and the captured log lines; clean up the case's files.

### 5.2 The cut

clumsy started with command-line options (names from clumsy's source, `src/main.c`, `drop.c`, `lag.c`; parameters
make it start filtering on launch):

```
--filter "<filter>" --drop on --drop-inbound on --drop-outbound on --drop-chance 100
```

(and for lag: `--lag on --lag-inbound on --lag-outbound on --lag-time <ms>`). The filter contains spaces: quote it.

- Default filter: `(tcp.DstPort == 445 or tcp.SrcPort == 445) and (<address terms>)`, where the address terms are
  `ip.DstAddr == a or ip.SrcAddr == a` for each IPv4 address and `ipv6.DstAddr == b or ipv6.SrcAddr == b` for each IPv6
  address of the server (`Dns.GetHostAddresses(serverName)`; strip any `%zone` suffix). SMB to this server is
  observed over both IPv4 and IPv6.
- `--port-only`: `tcp.DstPort == 445 or tcp.SrcPort == 445` (also cuts SMB to any other server: say so on screen).
- If the address-narrowed filter fails the cut confirmation, abort with a message suggesting `--port-only` (the
  address syntax may be what failed).

### 5.3 Proving the rig works (self-checks)

- **Cut confirmed** = a fresh TCP connect to the server on port 445 fails or times out (2 s) **on every resolved
  address**, checked repeatedly from the moment clumsy is started; record the time of the first confirmed check.
  All case timings are measured from that moment.
- **Restored** = 5.1 step 1 passes.
- **Two controls, run first. If either fails, stop and report "test rig not working"; report no TermPoint verdicts.**
  - *Known-good control:* `NetworkFileOps.ExistsAsync(<probe folder>\no-such-file)`, cut before the call: must
    return `Completed == false` within `TimeoutMs + 1.5 s` of the cut being confirmed (this path was proven in the July
    field test).
  - *Known-bad control:* an UNGUARDED plain call, `File.WriteAllBytes(<probe folder>\control.bin, 64 KB)` on a
    dedicated background thread, cut before the call: must still be blocked 15 s after the cut was confirmed. If it
    completes sooner, the cut is not really blocking SMB (for example a cache answered), so nothing else can be
    trusted. Abandon the thread; never join it on the main path.

### 5.4 Cutting mid-operation (deterministic, no guessing)

For the long operations (copy, hash), the cut must land while the operation is demonstrably in progress:

1. Start clumsy with **lag only** (`--lag-ms`) so the operation is slow.
2. Start the operation (do not await yet; keep the task).
3. Wait until it is clearly under way: at least 1.5 s elapsed, the task not completed, and for the copy the
   destination file on the share already larger than 0 (check its size through a plain `FileInfo` on a background
   thread with a 2 s timeout; the lag makes this slow but fine).
4. Stop the lag clumsy and start the drop clumsy (one restart: about 1 s with no impairment, harmless here).
5. Confirm the cut (5.3). **If the task completed before the cut was confirmed, the case is `NOT TESTED (finished
   before the cut)`** — retry once with `--big-mb` doubled; never report that as a pass.

### 5.5 Test files

- A small text file (a few hundred bytes) on the share, for read/write/move/delete/exists/stat cases.
- A large local file of `--big-mb` random bytes (local temp) as the copy source, and a large file of the same size on
  the share for the hash case (uploaded during set-up with the network normal).
- A small valid SQLite database for the integrity case: create it locally with `Microsoft.Data.Sqlite` (one table,
  a few rows; close the connection; `SqliteConnection.ClearAllPools()`), copy it to the share during set-up.

### 5.6 What is measured for each case

| Field | How |
|---|---|
| `call-return` | Stopwatch around the method call itself, until it hands back its `Task` (before awaiting). `async` methods run synchronously up to their first `await`; if this is more than 1 s the call can block whatever thread called it (the UI thread, for some callers). Flag > 1 s. |
| `result-after-cut` | Time from the cut being confirmed until the task completes. If it has not completed when the hold ends, keep waiting after the restore (cap 120 s) and record "completed N s AFTER the network came back" (the A1 signature) or "never completed". |
| `result` | The method's return value (`Completed`, the bool, hash null or not, the probe kind). |
| `log` | The lines `NetworkFileOps` logged during the case (from the probe's logger). |
| `landed-in` | For mid-operation cases: which step the stall was reported in, from the log line ("CopyFile (write)", "CopyFile (close)", "ComputeHash (read)", …), or "nothing logged". |
| `recovery` | Seconds from stopping clumsy until the share answered again. |

**Verdict per case:**
- **PASS:** the task completed within `TimeoutMs + 2 s` of the cut being confirmed, reporting the timeout/stall
  (`Completed == false`, `false`, null hash, unreachable…).
- **FAIL:** it completed only after the network came back, or after more than `TimeoutMs + 2 s`, or never; or
  `call-return` > 1 s (reported as its own failure: "the call blocks its caller").
- **NOT TESTED:** cut not confirmed, operation finished before the cut, or a control failed.

## 6. Case matrix

Cut **before** the call (CB), each once:

| Case | Call |
|---|---|
| `exists` | `ExistsAsync(file)` |
| `dir-exists` | `DirectoryExistsAsync(folder)` |
| `probe-file` | `ProbeFileAsync(file)` |
| `stat` | `RunAsync(() => { var i = new FileInfo(f); return (i.Length, i.LastWriteTimeUtc); }, "File.Stat")` (the save's stat of D) |
| `read-text` | `ReadAllTextAsync(file)` |
| `write-text` | `WriteAllTextAsync(file, "x")` |
| `move` | `MoveAsync(fileA, fileB, overwrite: true)` (the save's rename and the heartbeat's) |
| `delete` | `DeleteAsync(file)` |
| `hash-cb` | `ComputeHashAsync(big file on share)` |
| `copy-cb` | `CopyAsync(local big file, share\copy.bin)` |
| `integrity` | `CheckIntegrityWithSummaryAsync(small db on share)` (and `CheckIntegrityAsync` if cheap to add) |

Cut **mid-operation** (CM, 5.4), each `--repeat` times:

| Case | Call | Why |
|---|---|---|
| `copy-cm` | `CopyAsync(local big file, share\copy.bin)` | **The A1 suspect.** A write on an open handle after the cut. |
| `hash-cm` | `ComputeHashAsync(big file on share)` | The read path; the field saw this caught correctly (control for the A1 comparison). |

`--list` prints this matrix.

## 7. Static gap audit (part of the build, not of the run)

The probe only sees calls that go through `NetworkFileOps`. Read these files and list every direct file-system or
SQLite access that can touch a path on the share (the database D, its `.tmp`, its `.lock`, the lock `.tmp`, backups
on a share, the recovered-copy destination) **without** a `NetworkFileOps` deadline:

`src/TermPoint/Services/CheckoutService.cs`, `WriteLockService.cs`, `SessionLifecycle.cs`, `BackupService.cs`,
`DatabaseRestore.cs`, `NewFilePlacement.cs` (if present), `DatabaseValidator.cs` (if present), `FolderAssessor.cs`,
`MainWindow.axaml.cs`, plus anything else that holds `SourcePath` or a lock path (search for `File.`, `Directory.`,
`FileInfo`, `FileStream`, `new SqliteConnection`).

For each hit: file:line, the call, what path it touches, and a verdict: **local only** / **share, guarded elsewhere
(say where)** / **share, UNGUARDED (could wait for the SMB timeout)**. Put the list in
`tools/NetworkProbe/GAP-AUDIT.md`, unguarded ones first. Do not fix anything.

## 8. Output

- Console: one line per case as it finishes, then a summary table (case, mode, verdict, call-return, result-after-cut,
  landed-in, recovery). Controls first.
- Markdown report in `--report-dir`: date, computer, server, filter used, clumsy path and pid, the controls, the table,
  and per case the captured log lines. Plain words at the top: "N passed, N failed, N not tested; the rig's
  self-checks passed / failed".

## 9. Deliverables for the builder

- `tools/NetworkProbe/NetworkProbe.csproj` (net10.0, Exe, `ProjectReference` to `..\..\src\TermPoint\TermPoint.csproj`,
  `Microsoft.Data.Sqlite` if needed for 5.5).
- The program, split into small classes (options, clumsy control, cut verification, logger, cases, report), with the
  repo's comment style (XML doc comments on every method: purpose, parameters, return, exceptions; liberal comments).
- `tools/NetworkProbe/README.md`: what it answers and what it doesn't, how to run (elevated, TermPoint closed, the
  command line), how to read the report, safety notes, the App static-initialiser side effects found.
- `tools/NetworkProbe/GAP-AUDIT.md` (section 7).

**The builder must:**
- build it: `dotnet build tools/NetworkProbe/NetworkProbe.csproj -c Release` (note: `src/TermPoint` multi-targets
  `net10.0;net10.0-browser`; the reference must resolve to `net10.0`; if Visual Studio locks files, build to
  `-o %TEMP%\netprobe-build`);
- run `--list` and `--dry-run <a local temp folder>` and include the output in its report;
- **not** run the real probe (it needs elevation, clumsy and the live share; the user runs it);
- **not** start clumsy, **not** modify anything under `src/`, **not** commit, **not** create branches.
