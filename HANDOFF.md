# Handoff: TermPoint 1.2.3, 2026-10-27 late evening (dev machine) — A8 fixed (item 32), M8 passed, re-runs next

Written at the end of a long session so a fresh conversation can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so everything a new agent needs is here or in the files this
points to.

**To start the new agent, say:** "Read `HANDOFF.md`, then help me finish the 1.2.3 field tests."

## Where things are

- **Branch `Version_1.2.3`**, committed (not pushed) with spec item 32, `<Version>` **1.2.3.3**, and today's doc
  updates. Never create branches; commit and push only when Greg asks.
- **Sources of truth:** `docs/testing/network-field-tests-1.2.3.md` (**"Plan from here" table near the top**, status
  table, Open issues, each scenario's results); `spec_version_1.2.3.md` (**item 32** = the A8/A2 fix, told as
  Alice-and-Bob stories); `spec_version_1.2.4.md`.
- **Test builds (sideload MSIX, same signing cert, already trusted on both computers):**
  `\\mekler\Users\gregs\temp\msix-1.2.3.3\` (current), `msix-1.2.3.2\`, `msix-1.2.2\` (built from 9b4aa6b for M8);
  local copies in `C:\temp\termpoint-msix-<version>\`. Delete these folders after the field tests. Before the
  Store/Velopack release build, `<Version>` must go back to three numbers (consider 1.2.4 so test machines update).

## What happened on 2026-10-27 (evening)

- **A8 decided and fixed in 1.2.3 as spec item 32**, changes (a) + (c) + (d), design approved by Greg:
  (a) a lock that names another session is final, no 200 ms re-read; (c) the heartbeat renews only a lock that names
  this session, otherwise writes nothing and asks for the ownership check at once (also fixes A2); (d) the save's final
  check deletes D.tmp only when it confirmed the lock is ours. Sonnet coded, Opus reviewed. Both compile checks pass;
  full suite with harness green (the harness's "heartbeat ticked" step in `Tests/Harness/Computer.cs` now waits for the
  ownership check the tick starts, as the wake step does). New exit-path finding (pre-existing, no data risk) went to
  the 1.2.4 list.
- **M8 passed** (Dir 1, 2, 3, gate) on GregDeskTop 1.2.3.2 + GregSurface 1.2.2.
- **Field-test plan reset** for item 32: the logs show only M5 (3), M5 (4), M6 and M7 touched the changed code, so
  every other pass stands.

## Next steps (in order)

1. **Install 1.2.3.3.** GregDeskTop: close TermPoint, double-click the 1.2.3.3 MSIX (installs over 1.2.3.2).
   GregSurface: it is on **1.2.2** — copy its log first
   (`Copy-Item "$env:APPDATA\TermPoint\Logs\app-2026-10-27.log" "\\mekler\Users\gregs\temp\surface-logs\app-2026-10-27-v122.log" -Force`),
   uninstall, install 1.2.3.3 (fresh settings: the wizard appears). Check both title bars say v1.2.3.3.
2. **Reset D from `BIOL-TT-master.db`** (not the M8 copy) — see "Reset" below.
3. **Re-runs on 1.2.3.3** (the only Musts left): M5 (3) (frozen process; expect the "taken over" banner within about a
   minute of resuming), **M5 (8)** (new: wake with Wi-Fi off; banner within a minute of Wi-Fi returning), M6 (expect
   log "lock lost, D.tmp left for the new holder's cleanup"), M7 (the heartbeat now notices the script's takeover by
   itself), M5 (7) (clock skew). Expected log lines are in item 32's "For the briefs" and in each scenario.
4. Then the Shoulds (S2, S3, S7–S9), N3 and S11 on 1.2.3.3, N1/N2 optional; wrap-up checklist at the end of the doc.
5. Still open for Greg's placement (1.2.3 or 1.2.4): A3–A7, A9–A11 (Open issues in the field-test doc).

## How the field tests are run (know-how)

- **Test database:** `\\mekler\Users\gregs\temp\termpointdata\BIOL-TT.db`, master copy `BIOL-TT-master.db` beside
  it. **Reset** (TermPoint closed on both computers; check no `BIOL-TT.lock`): copy the master over `BIOL-TT.db`,
  delete `BIOL-TT.lock` / `.lock.tmp` / `.db.tmp` / `-wal` / `-shm`, compare hashes. Also there: M8's
  `BIOL-TT-m8-master.db` / `-m8-gate.db`, `BIOL-TT (day1-end).db`, recovered and "before restore" copies (can stay).
- **Claude's shell sees the share only outside the sandbox** (`dangerouslyDisableSandbox: true`, PowerShell); Git
  Bash can't reach `//mekler/...`. Never open D with SQLite in place: copy it to the scratchpad and query the copy
  (`py -3.11 -I`, `?mode=ro`). A compare script like the session's `m8_check.py` (diff a copy of D against a reference
  copy, row by row, plus integrity check) is the quickest way to verify a step.
- **Logs:** GregDeskTop `%APPDATA%\TermPoint\Logs\` (multi-line entries: the time is on a line before `Context :`).
  GregSurface: Greg copies its log to `\\mekler\Users\gregs\temp\surface-logs\`.
- **"Sleeps":** GregSurface has no lid; Start → Sleep, but Modern Standby self-wakes after ~4–5 min and renews the
  lock. For takeover tests **freeze TermPoint instead** (Resource Monitor → CPU → right-click TermPoint.exe →
  Suspend / Resume Process). Sample the lock file every 20 s to watch the heartbeat stop; "go" after 4 minutes.
- **Clumsy** (filter `tcp.DstPort == 445 or tcp.SrcPort == 445`): degraded = Lag 110 ms + Drop ~2 %; Lag 110 alone
  for slow saves. **Open TermPoint before starting clumsy** (A6). Time a 1 MB write instead of ping.
- **Distinct section codes** per scenario; Greg's typing varies (`ZZZT 1004 D`), so diff against a reference copy.
- **Startup:** GregDeskTop opens the most recent database automatically; use Files → Open for others.
- **MSIX:** a lower version can't install over a higher one; uninstall first (deletes logs and kept changes).
  `build-msix.ps1` always writes `C:\temp\termpoint-msix` (copy a build aside before the next one). C: has ~2 GB free:
  delete `C:\temp\termpoint-msix\publish` after a build.

## Working agreements (Greg's)

- **Designs as Alice-and-Bob stories** (Bob loses the lock / sleeps, Alice takes over, Carol is a third computer),
  today vs. with the fix; technical detail after, compact. Otherwise Greg is "buried in verbiage".
- **Opus orchestrates, Sonnet codes.** Opus writes a self-contained brief; Sonnet implements (no commits, no branches,
  no .md/.axaml edits); Opus reviews the diff, runs both compile checks, the full suite and (for persistence work) the
  harness; Greg hand-checks; commit and push only when Greg asks. Never create branches.
- **User-facing wording is approved by Greg** before coding. **Warn before editing any `.axaml` file.**
- **Persistence work** is checked against the four rules in `CLAUDE.md` before the layout; ask the
  `MinimumVersionToWrite` question every release (1.2.3: No, also for item 32).
- **Debugging:** with competing hypotheses, propose the cheapest probe first.
- **Keep replies tight;** Greg is often low on tokens. Offer a handoff early in long sessions.

## This machine (GregDeskTop, the dev computer)

- **C: is nearly full** (~2 GB free). Tests only via **`tools/run-tests-lowdisk.sh`** (Git Bash); full suite ~4 min;
  with `TERMPOINT_HARNESS=1` ~17 min (it spawns its own extra testhost processes for `RandomWalkShardTests` — those
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
