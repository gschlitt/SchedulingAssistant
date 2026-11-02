# Handoff: TermPoint 1.2.3 RELEASED on the Microsoft Store, 2026-10-28 (dev machine)

Written at the end of a long session so a fresh conversation can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so everything a new agent needs is here or in the files this
points to.

**To start the new agent, say:** "Read `HANDOFF.md`, then help me start 1.2.4."

## Where things are

- **1.2.3 is released on the Microsoft Store** (Store only; Velopack comes later). Built from **`cf4d4cb`**
  (`<Version>` 1.2.3, MSIX 1.2.3.0, the "What's new" banner in `AppAnnouncements.cs`). **`master` and `Version_1.2.3`
  are pushed at `cf4d4cb`**; no `v1.2.3` tag (pushing a `v*` tag starts the macOS workflow; pushing `master` deploys
  the WASM demo, so Greg checks the demo locally first). Later commits on `Version_1.2.3` are docs only. Never create
  branches; commit and push only when Greg asks.
- **Store package:** `\\mekler\Users\gregs\temp\store-1.2.3\TermPoint.msix`. Store identity values are in the dev
  console's `config.ps1` (see "This machine").
- **Sources of truth:** `spec_version_1.2.4.md` (**next release**; the new section "Release and distribution" holds the
  Store-update failure below); `docs/testing/network-field-tests-1.2.3.md` (every 1.2.3 result, Open issues A1–A13,
  and the Wrap-up's list of accepted gaps); `designs/spec_version_1.2.3.md`.
- **Test builds to clean up:** sideload MSIX folders `\\mekler\Users\gregs\temp\msix-1.2.2\` … `msix-1.2.3.4\` and
  `C:\temp\termpoint-msix-1.2.3.4`; the sideload TermPoint (`AcademicSolutions.TermPoint`) is still installed on
  GregDeskTop beside the Store app (removed from GregSurface).
- **`MinimumVersionToWrite`:** No for 1.2.3 (unset). The Store's "mandatory" flag does nothing by itself: TermPoint
  doesn't read it (in-app Store updates are parking-lot P7).

## What happened on 2026-10-28 (release day)

- **Full harness re-run on `79a5139` clean** (4057 passed, 3 skipped, 0 failed), so A12's crash-sweep failure was
  timing. **S2 and S3 passed** on 1.2.3.4; S2 found **A13** (an extra, wrong "Checkout Failed" message when the
  read-only copy fails at open; on the 1.2.4 list). The other Shoulds and Nices were accepted as gaps (field-test doc,
  Wrap-up).
- **Release:** `<Version>` 1.2.3, What's new banner (Greg's wording), WASM demo checked locally, `master` pushed, Store
  package built and submitted; Microsoft certified and released it.
- **After the release: the Store version wouldn't open until the computer was restarted** (GregDeskTop after the
  update from Store 1.2.2; GregSurface after a fresh install). Root cause found on GregDeskTop: a Windows bug where a
  stale, empty family record stays in Windows' package cache because removing the old version couldn't delete a file
  that Windows shares between apps (`Microsoft.DiaSymReader.Native.amd64.dll`, held by Phone Link). Full evidence and
  the 1.2.4 actions: `spec_version_1.2.4.md`, "Release and distribution". Users' workaround: restart.

## What happened on 2026-10-27 (night)

- **1.2.3.3 installed on both. M7 passed** (the heartbeat noticed Alice's takeover 11 s later, by itself).
- **M5 (3) on 1.2.3.3: A8 fixed, but found A12.** On waking after a takeover the wake check and the heartbeat both
  find the new holder's name (item 32 (c)) and both demoted. Harmless with unsaved changes; **with none, R4 broke**
  (probe): "The database connection could not be restored…" and a red log-text banner instead of the takeover
  banner. No data risk.
- **A12 fixed in 1.2.3.4** (Greg approved; Sonnet coded, Opus reviewed): `HandleLockLossAsync` raises `WriteLockLost`
  at most once per write session (`_lockLossRaised`), re-armed by `StartWatchingForLockLoss()` at the three places
  write access begins (`CheckoutAsync`, `ForceCheckoutAsync`, `FinishRecoveryCheckoutAsync`). Seven new `_A12` tests,
  all failing without the fix. Both compile checks pass. Full suite with harness: 4056 passed, 3 skipped, **1 failed:
  `CrashSweep_EveryPointIsCutOrExplained`** ("planned cut did not fire", a harness timing check; passes alone 5/5;
  the safety check `CrashSweep_NoNewViolations` passed). Explanation in A12's entry.
- **On 1.2.3.4 every remaining Must passed:** M5 (3) with and without unsaved changes, M5 (8), M6, M5 (7).
- **M6 reached the exit-path finding already on the 1.2.4 list** (demotion while the window closes: ERROR log line,
  no banner, no data risk). Greg: stays in 1.2.4.

## Next steps (in order)

1. **Microsoft support case** for the Store-update failure, with the evidence in `spec_version_1.2.4.md` ("Release
   and distribution"); the Process Monitor export `C:\temp\temp.CSV` on GregDeskTop goes with it. Watch for users
   reporting "TermPoint won't open" (the answer: restart the computer).
2. Restart GregDeskTop (it was kept in the failing state for the diagnosis), check the Store TermPoint opens, then
   uninstall the sideload TermPoint and delete the test MSIX folders. Check GregSurface's clock is on automatic time.
3. **N4** (the Store update link) now that 1.2.3 is certified.
4. **Plan 1.2.4** from `spec_version_1.2.4.md`. Most urgent from the release: drop `Microsoft.DiaSymReader.Native.amd64.dll`
   from the package, use **package flights** to our own computers before releasing, and look at the failing GitHub
   Tests workflow. Also open for placement: A3–A7, A9–A11, A13 and the exit-path demotion seen in M6.
5. **1.2.4 candidates not yet in `spec_version_1.2.4.md`:** the harness's `SleepEvent` should run the wake check and
   the heartbeat together, as a real wake does (that gap is how A12 got past the tests); harden
   `Computer.SettleBackgroundWorkAsync` (wait for the demotion to be queued after a "no longer ours" / "genuinely
   lost" line) if the crash-sweep timing failure recurs; `ReleaseAsync` unsubscribes only `OnWake`, so after a release
   and re-checkout `OnHeartbeatFailed` is subscribed twice (harmless).

## How the field tests are run (know-how)

- **Test database:** `\\mekler\Users\gregs\temp\termpointdata\BIOL-TT.db`, master copy `BIOL-TT-master.db` beside
  it. **Reset** (TermPoint closed on both computers; check no `BIOL-TT.lock`): copy the master over `BIOL-TT.db`,
  delete `BIOL-TT.lock` / `.lock.tmp` / `.db.tmp` / `-wal` / `-shm`, compare hashes (master `925AD29A…`). The folder
  also holds recovered copies (`(recovered-…)`), M8 copies and GregSurface's backups (it keeps them beside D).
- **Claude's shell sees the share only outside the sandbox** (`dangerouslyDisableSandbox: true`, PowerShell); Git
  Bash can't reach `//mekler/...`. Never open D with SQLite in place: copy it to the scratchpad and query the copy.
- **Helper scripts on GregDeskTop** (`tmp/` is gitignored, so they are not on other machines):
  - `tmp\lockwatch.ps1`: samples the lock file every 20 s (holder, session, heartbeat, D's time, whether `D.tmp`
    exists). Run in the background (outside the sandbox):
    `pwsh -NoProfile -File tmp\lockwatch.ps1 -Out <scratchpad>\lockwatch.txt -Minutes 90`. A Monitor on its last line
    gives "go" once GregSurface's heartbeat has stood still for 4 minutes; it also proves who rewrote the lock.
  - `py -3.11 -I tmp\zzzt_check.py <copy.db> …`: lists the `ZZZT` sections and the integrity check of each copy.
  - `tmp\handtest_lock.py takeover|stale|clear <db>` for M7 (Alice).
- **Logs:** GregDeskTop `%APPDATA%\TermPoint\Logs\` (multi-line entries: the time is on a line before `Context :`).
  GregSurface: Greg copies its log to `\\mekler\Users\gregs\temp\surface-logs\` (usually as `app-<date>.log`,
  overwriting the last copy; the 1.2.2 log from M8 was overwritten, M8 already recorded).
- **"Sleeps":** GregSurface has no lid; Start → Sleep self-wakes after ~4–5 min and renews the lock unless its network
  is off. For takeover tests **freeze TermPoint instead** (Resource Monitor → CPU → right-click TermPoint.exe →
  Suspend / Resume Process). The freeze can take effect later than it seems: trust the lock samples, not the moment.
- **Clumsy** (filter `tcp.DstPort == 445 or tcp.SrcPort == 445`): degraded = Lag 110 ms + Drop ~2 %; Lag 110 alone
  for slow saves (M6: ~15 s exit save). **Open TermPoint before starting clumsy** (A6). Time a 1 MB write instead of
  ping.
- **Distinct section codes** per scenario; Greg's typing varies, so diff against a reference copy.
- **Startup:** GregDeskTop opens the most recent database automatically; use Files → Open for others.
- **MSIX:** a lower version can't install over a higher one; uninstall first (deletes logs and kept changes).
  `build-msix.ps1` always writes `C:\temp\termpoint-msix` (copy a build aside before the next one). C: has ~1.8 GB
  free: delete `C:\temp\termpoint-msix\publish` after a build; don't build while the harness runs.

## Working agreements (Greg's)

- **Designs as Alice-and-Bob stories** (Bob loses the lock / sleeps, Alice takes over, Carol is a third computer),
  today vs. with the fix; technical detail after, compact. Otherwise Greg is "buried in verbiage".
- **Opus orchestrates, Sonnet codes.** Opus writes a self-contained brief; Sonnet implements (no commits, no branches,
  no .md/.axaml edits); Opus reviews the diff, runs both compile checks, the full suite and (for persistence work) the
  harness; Greg hand-checks; commit and push only when Greg asks. Never create branches.
- **Greg worries about fix-on-fix cascades** (2026-10-27): before any further change in the lock/save paths, check
  what else runs at the same moment (wake check, heartbeat, save, exit save, reader poll) and say how the tests cover
  it. Field-test doc edits mid-run were stopped once; record results when Greg asks.
- **User-facing wording is approved by Greg** before coding. **Warn before editing any `.axaml` file.**
- **Persistence work** is checked against the four rules in `CLAUDE.md` before the layout; ask the
  `MinimumVersionToWrite` question every release.
- **Debugging:** with competing hypotheses, propose the cheapest probe first.
- **Keep replies tight;** Greg is often low on tokens. Offer a handoff early in long sessions.

## This machine (GregDeskTop, the dev computer)

- **Dev console** (Greg's build panel): `C:\Users\gregs\source\repos\dev-console\dev-console.bat`, beside the repo.
  Menu tasks: 01 sideload MSIX, 02 Store MSIX, 03 commit/push TermPointPages, 04/05 licenses, 06/07 WASM, 08 macOS.
  Its `config.ps1` holds the Store identity values.
- **Diagnosing a packaged app that won't open:** `IApplicationActivationManager` gives the real HRESULT (a click shows
  nothing); Process Monitor filtered on the package family name shows what Explorer looked up.

- **C: is nearly full** (~1.8 GB free). Tests only via **`tools/run-tests-lowdisk.sh`** (Git Bash); full suite ~4 min;
  with `TERMPOINT_HARNESS=1` ~20 min (it spawns its own extra testhost processes for `RandomWalkShardTests` — those
  are not strays). Delete `$TEMP` test logs and `.trx` after.
- **Compile check:** `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`, and the
  same with `-f net10.0-browser`. (A Release restore leaves Debug-only packages unresolved for `--no-restore` Debug
  builds; `dotnet restore src/TermPoint.Tests/TermPoint.Tests.csproj` fixes it, offline.)
- **Never kill processes by image name**; only by PID, and only your own.
- `python` here is Inkscape's; use **`py -3.11`**. Controlled Folder Access blocks test runners writing into
  `Documents`. Git Bash `sed -i` turns CRLF files into LF: check line endings after scripted edits.
- `src/TermPoint/Properties/launchSettings.json` is local: `TERMPOINT_DEBUG_DATE`, `TERMPOINT_DEBUG_SLOW_EXIT_SAVE`.
- `tools/ScrollAnchorProbe`: re-run after every Avalonia upgrade. `grep.exe.stackdump` in the repo root is untracked
  junk; never commit it.
