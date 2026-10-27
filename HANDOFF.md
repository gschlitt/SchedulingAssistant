# Handoff: TermPoint 1.2.3, 2026-10-27 evening (dev machine) — field tests, day 1 done

Written at the end of a long session so a fresh conversation can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so everything a new agent needs is here or in the files this
points to.

**To start the new agent, say:** "Read `HANDOFF.md`, then help me finish the 1.2.3 field tests."

## Where things are

- **Branch `Version_1.2.3`**, pushed through 5b4c3bc. **Uncommitted** (Greg decides when to commit): `<Version>` bumped
  to **1.2.3.2** in `src/TermPoint/TermPoint.csproj`; today's results in `docs/testing/network-field-tests-1.2.3.md`;
  additions to `spec_version_1.2.4.md`; this file. Never create branches; commit and push only when Greg asks.
- **Field-test build 1.2.3.2** (sideload MSIX from `build-msix.ps1`, output `C:\temp\termpoint-msix\`) is installed on
  GregDeskTop and GregSurface. Before the Store/Velopack release build `<Version>` must go back to three numbers
  (consider 1.2.4 so field-test machines see the release as an update).
- **Sources of truth:** `docs/testing/network-field-tests-1.2.3.md` (status table at the top, **Open issues A1–A11**,
  each scenario's result with times and log lines); `spec_version_1.2.3.md`; `spec_version_1.2.4.md` (now also holds the
  leftover-process item and field evidence on the heartbeat and `.tmp` items); `designs/data-persistence-review-2026-10.md`.

## Field-test status (all Musts run on 1.2.3.2 except where noted)

- **Pass:** M1, M2, M3, M4 (B1–B3), M6 (F12: final check refused; work kept), M7 (F4), M9, M10 (re-run), M11 (a)–(d),
  M12; Shoulds S1, S4 (with M10), S6.
- **M5:** (1), (2), (4), (5) pass; **(3) and (4) failed on waking = A8**; (6) not run (no third computer); **(7) clock
  skew not yet run**.
- **Not yet run:** **M5 (7)**, **M8** (mixed 1.2.2/1.2.3; needs a 1.2.2 sideload build from commit `9b4aa6b` "1.2.2"
  — no tag; build in a temp worktree, copy the 1.2.3.2 MSIX aside first because `build-msix.ps1` always writes
  `C:\temp\termpoint-msix`; uninstall 1.2.3 on GregSurface first, collecting its logs), then Shoulds S2, S3, S7–S11,
  Nice N1–N3.

## The open decision: A8 (Greg to decide; the main thing for the next session)

**A8:** after a takeover, the computer that slept wakes and keeps acting as writer until its next save (or autosave).
Wake check (`CheckoutService.OnWake` → `VerifyLockIsOursAtAsync`): first read finds another session's lock (NotOurs),
the 200 ms re-read collides with a file in use (most likely its own heartbeat's rename, which fires at the same moment
after a wake), the collision is classed Unreachable, and the wake check "keeps current state" with no second try. Seen
3 of 3 times (M5 (3), M5 (4), and M6's final check, where it also made the save delete its own `.tmp` as if the lock
were still its own). **No data risk** (every save checks the lock; work always kept); R4 fails for up to one autosave
interval. Not certain the collision is always on the second read: the first can collide too, and after a real sleep
where Wi-Fi drops the first check is usually Unreachable — both give up the same way.
**Options put to Greg:** (1) fix in 1.2.3: keep a definite first answer (a lock that parses and names another
session) when the re-read fails, **and** make the wake check retry on Unreachable (e.g. every 5 s up to 2 min); about
30–40 lines plus tests; then a 1.2.3.3 build and re-runs of M5 (3), M6 and a real sleep with Wi-Fi off. (2) Ship 1.2.3
with a release note; fix in 1.2.4 together with A2 (heartbeat ownership check, already on the 1.2.4 list as "9.3c-2b").
Either way: design + four-rules check + risk review first, Greg approves, then the usual Sonnet-codes workflow.

## Other findings from 2026-10-27 (details in the field-test doc's Open issues)

A2 heartbeat renews whoever's lock is in the file (on 1.2.4 list) · A3 section editor closes on demotion, unapplied
change dropped silently (item 14, 1.2.4) · A4 refused-write message queues behind other notices · A5 Restore/Discard
choice not logged · A6 a link with ~220 ms round trip can't open a database (integrity check of D, 5 s limit) · A7
backups: one per open, keep 5, so five opens wipe older ones · A9 a save with no edits right after "Can't Restore"
(stale `recoverUnsaved` flag) · A10 Save a Copy… suggests a name already taken · A11 a writer asleep mid-save blocks the
new writer's saves (shared `.tmp` name; on 1.2.4 list) · **Leftover process after a Save & Backup restore moved to
1.2.4** (no data risk; evidence and fix direction in `spec_version_1.2.4.md`, "Item 5 (M11)"). A2–A7, A9–A11 still
need Greg's 1.2.3/1.2.4 placement where not stated.

## How the field tests are run (know-how from day 1)

- **Test database:** `\\mekler\Users\gregs\temp\termpointdata\BIOL-TT.db` (not `FT.db`), master copy
  `BIOL-TT-master.db` beside it. **Reset** (TermPoint closed on both computers): copy the master over `BIOL-TT.db`,
  delete `BIOL-TT.lock` / `.lock.tmp` / `.db.tmp` / `-wal` / `-shm`, compare hashes. Old recovered and
  "before restore" copies sit beside it and can stay.
- **Claude's shell sees the share only outside the sandbox** (`dangerouslyDisableSandbox: true`, PowerShell); Git
  Bash can't reach `//mekler/...` at all. Never open D with SQLite in place: copy it to the scratchpad and query the
  copy (`py -3.11 -I`, `?mode=ro`). Reading the lock file and `Get-FileHash` on D are fine.
- **GregSurface's log:** Greg copies it to `\\mekler\Users\gregs\temp\surface-logs\` with
  `Copy-Item "$env:APPDATA\TermPoint\Logs\app-2026-10-27.log" "\\mekler\Users\gregs\temp\surface-logs\" -Force`
  (recreate the folder if it has vanished). GregDeskTop's log is local: `%APPDATA%\TermPoint\Logs\` (the sideload MSIX
  logs there, not under `LocalCache`).
- **"Sleeps":** GregSurface has no lid; Start → Sleep works but **Modern Standby self-wakes after ~4–5 min** and renews
  the lock. For reliable takeover tests **freeze TermPoint instead**: Resource Monitor → CPU → right-click
  TermPoint.exe → Suspend / Resume Process (TermPoint sees the same gap). Watch the heartbeat stop on the share (sample
  the lock file every 20 s in a background job) and say "go" after 4 minutes.
- **Clumsy** (filter `tcp.DstPort == 445 or tcp.SrcPort == 445`, Inbound+Outbound): degraded = **Lag 110 ms + Drop
  ~2 %** (4 % makes saves give up); Lag 110 only for slow-but-reliable saves (exit save ~15 s). **Open TermPoint
  before starting clumsy** (A6). Ping is not a check (ICMP); time a 1 MB write to the share instead.
- **Distinct section codes** per scenario (`ZZZT-9xx D` etc.); Greg's codes don't always match the suggestion (typos
  like `ZZT-902 S` or `ZZZT 922 D2`): diff the copy against the master rather than grepping one pattern.
- **Startup chooser:** GregDeskTop now has "Always open the most recent database at startup" **on** (set during a
  probe); the wizard is reachable from the chooser's Create new → Start Setup Wizard (no settings reset needed). The
  sideload MSIX keeps its settings somewhere other than `%APPDATA%\TermPoint\settings.json` (that file is a stale test
  overwrite from 2026-10-14).
- **Backups** for GregDeskTop go to `C:\Users\gregs\Downloads` (one per open, five kept); older BIOL-TT backups from
  2026-10-22 are in `\\mekler\temp\backups`.
- Diagnostics tools installed this session: `dotnet-stack`, `dotnet-dump` (global tools). A full dump needs
  `setclrpath C:\temp\termpoint-msix\publish` (the app ships its own runtime) and `--type Full`; delete dumps after.

## Working agreements (Greg's)

- **Opus orchestrates, Sonnet codes.** Opus diagnoses and writes a self-contained brief; Sonnet implements (no
  commits, no branches); Opus reviews the diff, re-runs both compile checks and the full suite; Greg hand-checks;
  commit and push **only when Greg asks**. Never create branches.
- **Small briefs, one kind of change each.** **User-facing wording is approved by Greg** before coding.
- **Warn before editing any `.axaml` file** and ask Greg to close it in Visual Studio first.
- **Persistence work** (save, lock, exit, switch, restore, recovery) is checked against the four rules in `CLAUDE.md`
  before the layout; ask the `MinimumVersionToWrite` question for every release (1.2.3: No). Late in a release,
  changes whose gain is mainly speed or a better recovery chance go to the next release; the risk review comes with
  the design.
- **Debugging:** with competing hypotheses, propose the cheapest probe and ask Greg to run it before theorising.
- **Keep replies tight;** don't restate what was just said. Offer a handoff early in long sessions.

## This machine (GregDeskTop, the dev computer) — practical notes

- **C: is nearly full** (about 2 GB free). Run tests with **`tools/run-tests-lowdisk.sh`**; full suite about 3–4 min;
  the persistence harness only with `TERMPOINT_HARNESS=1` (about 20 min). Delete `$TEMP` test logs and `.trx` after.
- **Compile check:** `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`, and the
  same with `-f net10.0-browser`.
- **Never kill processes by image name**; only by PID, and only your own (Greg ends his own TermPoint processes).
- `python` here is Inkscape's; use **`py -3.11`**.
- Controlled Folder Access blocks test runners from writing into `Documents`.
- `src/TermPoint/Properties/launchSettings.json` is local (untracked): `TERMPOINT_DEBUG_DATE`,
  `TERMPOINT_DEBUG_SLOW_EXIT_SAVE` (seconds) for the exit guard.
- `tools/ScrollAnchorProbe`: re-run after every Avalonia upgrade.
- `grep.exe.stackdump` in the repo root is untracked junk; leave it or delete it, never commit it.
