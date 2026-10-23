# NetworkProbe

Does every real network call TermPoint makes hand control back within its deadline when the SMB connection
goes dark?

TermPoint's `NetworkFileOps` races every network file operation against a 5 second deadline
(`NetworkFileOps.TimeoutMs`). This tool calls the **real** `NetworkFileOps` methods (through a project reference;
nothing is re-implemented) against a **real share**, cuts this computer's SMB traffic with **clumsy** at a moment it
chooses itself, and measures whether each call gave up in time. The design is in
[`designs/network-probe-spec.md`](../../designs/network-probe-spec.md); the static half of the work is in
[`GAP-AUDIT.md`](GAP-AUDIT.md).

It is **not** part of `TermPoint.sln` and **not** built by CI, like `tools/ScrollAnchorProbe`.

## What it answers, and what it does not

| It answers | It does not answer |
|---|---|
| **Platform.** When the real SMB connection goes dark, does each `NetworkFileOps` call come back within `TimeoutMs`, reporting the timeout? In particular finding **A1**: the chunk write in `CopyCoreAsync` (`dst!.WriteAsync(...)`) is raced only *after* `WriteAsync` returns its task, so a write that Windows performs synchronously during an outage could block the copy until the network returns. | **Logic.** Whether TermPoint then reacts correctly (banner, stays writer, keeps the work, recovers). The automated harness covers that (`NetworkFaults` timeouts at every step, `CrashPoints`, pause sweeps). |
| Whether a call **blocks its caller** (the time `NetworkFileOps` takes just to hand back its `Task`; over 1 s can freeze a UI thread). | Network access that **bypasses** `NetworkFileOps`. The probe cannot see it; the static audit in `GAP-AUDIT.md` lists it. |

## Before you run it (real run)

- **Windows, x64, an elevated terminal** ("Run as administrator"). The probe refuses otherwise: clumsy needs it, and
  a non-elevated clumsy relaunches itself elevated, after which the probe could not stop it.
- **TermPoint closed** on this computer (it would see the outage too). The probe refuses if a `TermPoint` process
  runs, and never closes it for you.
- **No clumsy running.** clumsy allows one instance; the probe refuses rather than stop a clumsy it did not start.
- **clumsy.exe, x64 build.** Found automatically in Downloads, Desktop, Documents, `C:\tools` and Program Files
  (depth 4), keeping only a build for this computer's processor; or pass `--clumsy <path>`.
- **A share on ANOTHER computer.** `--share \\server\share\folder` must be a UNC path (a drive letter hides the
  server's name, which the probe needs to build the filter) and must already exist. The probe creates and works only
  in its own `netprobe-<yyyyMMdd-HHmmss>` subfolder there and deletes it at the end. It refuses a share on this
  computer.
- **Disk space.** The large test file is `--big-mb` (default 32) MB, in this computer's temp folder, and again in
  the probe's folder on the share (twice, for the copy and hash cases). A retry can double it, up to 256 MB. Build
  output is about 140 MB under `tools\NetworkProbe\bin` (delete it when done; it is git-ignored).

## Build

```
dotnet build tools\NetworkProbe\NetworkProbe.csproj -c Release
```

`src/TermPoint` multi-targets `net10.0;net10.0-browser`; the reference is pinned to `net10.0`. The project builds for
this computer's own runtime only (`win-x64`), which keeps the output to about 140 MB instead of about 600 MB. If
Visual Studio holds files (MSB3026/MSB3027), build to another folder: `-o %TEMP%\netprobe-build`.

Building also builds `src\TermPoint` (Release) into its normal git-ignored `bin\Release` and `obj` folders.

## Run

```
tools\NetworkProbe\bin\Release\net10.0\win-x64\NetworkProbe.exe --list
tools\NetworkProbe\bin\Release\net10.0\win-x64\NetworkProbe.exe --dry-run %TEMP%\netprobe-try
tools\NetworkProbe\bin\Release\net10.0\win-x64\NetworkProbe.exe --share \\mekler\Users\gregs\temp\handtest
```

(`dotnet run --project tools\NetworkProbe -c Release -- <options>` does the same.)

**Do a short first run to check the rig** (about 3 minutes):

```
NetworkProbe.exe --share \\mekler\Users\gregs\temp\handtest --only exists,copy-cm --repeat 1 --hold 20
```

A full run is about 25 to 40 minutes: 13 cut-before-call cycles (11 cases plus the optional boolean integrity
variant plus the first control) at roughly 70 s each (11 s cache settle, the cut, the hold, SMB reconnecting), the
known-bad control, and 6 mid-operation cycles. Lower `--hold` to 20 to shorten it.

### Options

| Option | Default | Meaning |
|---|---|---|
| `--share <UNC folder>` | required | An existing folder on the server. |
| `--clumsy <path>` | auto-find | clumsy.exe (x64). |
| `--by-address` | off | Filter on port 445 AND the server's DNS addresses. The default is port 445 only, which also cuts SMB to any other server for the duration of each cut (the probe says so on screen). Port-only became the default after the first real run (2026-10-22): SMB used an IPv6 address of the server that DNS does not return, so the address filter leaked and the known-bad control failed. (`--port-only` is still accepted and changes nothing.) |
| `--hold <s>` | 40 | How long each cut is held (minimum 20: the known-bad control needs 15 s). Well above the 5 s deadline, so "gave up in time" and "waited for the network" cannot be confused. |
| `--big-mb <n>` | 32 | Size of the large file for the copy and hash cases. |
| `--lag-ms <n>` | 150 | Lag used to slow a long operation down before the cut. |
| `--repeat <n>` | 3 | Repetitions of each mid-operation case (SMB caching makes runs differ). |
| `--settle <s>` | 11 | **Addition to the spec.** Pause between creating a cut-before-call case's files and cutting, so SMB's file-information cache (about 10 s) expires. A call answered from that cache needs no network and would prove nothing; 0 turns it off. |
| `--only <a,b,...>` | all | Case names to run (`--list`). The two controls always run first. |
| `--report-dir <dir>` | `<repo>\tmp\netprobe`, else `.\netprobe-reports` | Where the Markdown report goes (`tmp/` is git-ignored). |
| `--dry-run <local folder>` | | Plumbing check: no clumsy, no server, no elevation; every case runs against a local folder and is reported `SIMULATED`, never pass or fail. The folder is created if missing and removed again. |
| `--list` | | Print the case matrix and exit. |

Exit codes: 0 clean (or a dry run that worked); 1 at least one case FAILED; 2 the rig did not work or the run stopped
early; 3 refused to start; 64 command-line error.

## What a run does

1. Refuses unless safe (elevated, TermPoint closed, no clumsy, a UNC share on another computer that exists).
2. Resolves the server's addresses, drops any that do not answer on port 445 right now, and builds the clumsy
   filter: `(tcp.DstPort == 445 or tcp.SrcPort == 445) and (ip.DstAddr == a or ip.SrcAddr == a or ... ipv6.DstAddr == b or ...)`.
   SMB to the server is seen over both IPv4 and IPv6, so both are cut.
3. Swaps TermPoint's `App.Logger` for a recorder (see "Side effects" below), then runs the **two controls**:
   - *known-good*: `ExistsAsync(no-such-file)`, cut before the call, must report a timeout within `TimeoutMs + 1.5 s`;
   - *known-bad*: an unguarded `File.WriteAllBytes` (64 KB) on its own thread, cut before the call, must still be
     blocked 15 s after the cut was confirmed.
   If either fails the rig is not working (a cache answered, the filter missed the traffic...): the run stops and
   reports **no TermPoint verdicts**.
4. Runs each case as its own cycle: wait until the share answers, create the files, cut, **confirm the cut** (a fresh
   TCP connect to port 445 fails on every server address), call, measure for `--hold` seconds, stop clumsy, wait for
   the share to answer again (recording how long), record, clean up.
5. For the mid-operation cases (`copy-cm`, `hash-cm`) it starts clumsy with **lag only**, starts the operation, waits
   until it is demonstrably in progress (at least 1.5 s, not finished, and for the copy the destination already
   larger than 0), swaps lag for the drop (one restart, about 1 s with no impairment), and confirms the cut. A call
   that finished before the cut is **NOT TESTED**, never a pass; it is retried once with the large file doubled, and
   the larger size is kept for the repetitions that follow.
6. Stops clumsy (by its process id), waits for the network, deletes both folders, writes the Markdown report.

All timings are measured from the moment the cut was first **confirmed**, on one computer, with one clock.

## How to read the report

The report starts with plain words: `N passed, N failed, N not tested; the rig's self-checks passed / failed`. Then the
run details (server, filter, clumsy path and process ids), the controls, the table, and per case the log lines
`NetworkFileOps` wrote.

| Column | Meaning |
|---|---|
| `call-return` | How long the call took just to hand back its `Task` (before awaiting). Over 1 s is a failure of its own: the call can block whatever thread called it. |
| `result-after-cut` | Time from the cut being confirmed until the task completed. If the call outlived the hold, the probe keeps waiting after the network returns (up to 120 s) and says "AFTER the network came back": **that is the A1 signature**. |
| `landed-in` | Which step the stall was reported in, from TermPoint's own log line: `CopyFile (write)`, `CopyFile (close)`, `ComputeHash (read)`, ... or `nothing logged`. |
| `recovery` | Seconds from stopping clumsy until the share answered again. |

**Verdicts**

- **PASS**: completed within `TimeoutMs + 2 s` (7 s) of the cut being confirmed, reporting the timeout or stall (a
  call that threw within the window is also a pass, with the exception named).
- **FAIL**: completed only after the network came back, or later than 7 s, or never; or `call-return` over 1 s.
- **NOT TESTED**: the cut was not confirmed within 10 s; the operation finished before the cut; a control failed; or
  (an addition to the spec) the call **completed normally** after the cut, i.e. it was answered without the network,
  for example from SMB's cache, which proves nothing.

**What A1 looks like.** `copy-cm` FAIL with "completed N s after the cut, only AFTER the network came back" and
`landed-in` = `nothing logged`, while `hash-cm` PASSes with `landed-in` = `ComputeHash (read)`. A `copy-cm` that
PASSes with `landed-in` = `CopyFile (write)` refutes A1 for that run; run all three repetitions, because SMB caching
makes them differ.

**If the controls fail.** Known-bad finishing early means the cut is not blocking SMB for plain calls: try
`--port-only` (SMB multichannel can use server addresses that DNS does not return), and check the clumsy window.
Known-good failing with the known-bad passing means the 5 s race is not behaving as in the July field test. A cut that
is never confirmed on the very first try aborts with advice to use `--port-only` (the address syntax of the filter is
the likely cause).

## Safety

- **Only its own files.** It writes only inside its `netprobe-...` folder on the share and a `netprobe-local-...`
  folder in the temp folder. It never touches any `.db`, `.lock` or `.tmp` of TermPoint's. It cleans up both at the end
  (after the network is back) and says so if it could not.
- **Never leaves the network cut.** clumsy is started with `Process.Start`, the `Process` is kept, and it is stopped
  by that process id only, never by name. It is stopped in `finally`, on Ctrl+C, on a crash of the probe and on
  process exit. **Every clumsy pid is printed when it starts**; if the probe is ever stuck, end that pid in Task Manager.
  Ctrl+C once to stop cleanly (clumsy is stopped, the share is waited for, the folders are deleted); twice to leave at once.
- **The probe's rig calls are not `NetworkFileOps`.** Creating test files, uploading the large file, deleting folders
  and polling for the share all run on a thread-pool thread with their own deadline, so the rig itself can never
  hang on a dead share, and its calls never add lines to the log a case is judged on.
- **The call under test runs on a dedicated thread.** A call that blocks its caller for a minute cannot stop the
  probe from restoring the network on time.

## Side effects of touching `App` (found by reading, and by a dry run)

The first use of `App.Logger` runs `App`'s static initialisers. They were checked for side effects:

| Initialiser | Effect |
|---|---|
| `App.Logger` | Builds a `FileAppLogger` **and a live BugSnag client** (stage "production" in a Release build). **BugSnag's client hooks three process-wide events**: `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and `AppDomain.ProcessExit`. Clearing the logger's `BugsnagClient` (what the test project does) does **not** unhook them, so a crash of the probe would still be reported. The probe therefore removes the handlers the client added (found by comparing the lists before and after; removed through the events' public `-=`), and **refuses to run** if it cannot. |
| `App.LockService` | `new WriteLockService()`: reads this process's own start time (`LockIdentity.Current`). No timers, no files. |
| `App.IntegrityFaults`, the fault plans in `CreateCheckoutService` | Read `TERMPOINT_DEBUG_*` environment variables in a DEBUG build only; null in Release. |
| `App.Checkout` | `new CheckoutService(...)`: no folders or files. |
| `App.InstanceGuard` | `new DatabaseInstanceGuard(...)`: computes the `%AppData%\TermPoint\working\guards` path only; creates nothing until `TryAcquire`, which the probe never calls. |

Verified with a dry run: the newest file in `%AppData%\TermPoint\Logs` and the folder's own timestamp were unchanged
before and after, and nothing was created under `%AppData%\TermPoint`. `FileAppLogger.LogDirectoryOverride` (internal)
is set by reflection **before** `App` is touched, so even a line logged during initialisation would land in the temp
folder; `App.Logger` is then replaced through its backing field `<Logger>k__BackingField`, as
`src/TermPoint.Tests/RecordingAppLogger.cs` does. Both are commented in `LoggerRig.cs` as test-rig plumbing. Nothing
under `src/` was changed.

Late "abandoned ... task faulted after timeout" lines can arrive a minute after a case and show up in the *next*
case's log; the case's own verdict never depends on them.

## Files

| File | Job |
|---|---|
| `Program.cs`, `ProbeRun.cs` | Entry point; the whole sequence from refusals to report. |
| `ProbeOptions.cs`, `ProbeExceptions.cs`, `Preflight.cs` | The command line; the safety refusals. |
| `CaseCatalog.cs` | The case matrix: each case calls the real `NetworkFileOps` method. |
| `CaseRunner.cs`, `OperationRun.cs`, `Verdicts.cs`, `CaseResult.cs` | One case's cycle; the call on its own thread; the verdict rules; the recorded fields. |
| `RigControls.cs` | The two controls. |
| `ClumsyLocator.cs`, `ClumsyController.cs` | Finding clumsy (PE-header check); starting and stopping it by pid. |
| `CutTarget.cs`, `TcpReachability.cs`, `NetworkRigs.cs` | The filter; the independent "is it cut / is it back" witness; the real and the simulated rig. |
| `LoggerRig.cs`, `RecordingProbeLogger.cs` | The `App.Logger` swap and the recorder. |
| `ProbeWorkspace.cs`, `RigIo.cs`, `ProbeContext.cs` | The two folders and the test files; bounded rig-side file work; shared state. |
| `ReportWriter.cs`, `RunReport.cs`, `TextFormat.cs`, `ConsoleOut.cs`, `ProbeClock.cs`, `RepoLocator.cs` | Console and Markdown output (a closed console never stops the clean-up); the clock. |

## Things the first real run must check

These could not be verified without clumsy and a live share; see the report from the builder.

- clumsy's **command-line options** (`--filter`, `--drop on`, `--drop-inbound on`, `--drop-outbound on`,
  `--drop-chance 100`, `--lag on`, `--lag-inbound on`, `--lag-outbound on`, `--lag-time <ms>`) come from clumsy's
  source and from `tmp/handtest_blackhole.ps1`, not from a run of this program. If the clumsy window does not show
  the effect ticked, or the probe says "cut NOT confirmed", say so.
- The **IPv6 literals** in the filter (`ipv6.DstAddr == fe80::...`, zone id removed) are WinDivert filter syntax from
  its documentation. A link-local address may need `--port-only`. (The TCP checks keep the zone id, because a link-local
  address cannot be connected to without it.)
- Whether SMB **multichannel** or another address carries the traffic (then the known-bad control fails: use
  `--port-only`).
- Whether the 11 s `--settle` is enough to defeat SMB's caches (look for "answered without the network" in NOT TESTED
  reasons).
- That a **second** clumsy can be started right after the first is killed (the lag-to-drop swap), and how long its
  filter takes to engage (the probe waits 1.2 s after starting lag and then confirms the drop with TCP checks).
