# Handoff: TermPoint 1.2.3, 2026-10-27 (dev machine) — field tests

Written at the end of a long session so a fresh conversation can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so everything a new agent needs is here or in the files this
points to.

**To start the new agent, say:** "Read `HANDOFF.md`, then help me run the 1.2.3 field tests."

## Where things are

- **All 1.2.3 building is finished and hand-checked** (items built 2026-10-26, items 25 and 31 hand-checked
  2026-10-27). Nothing is left to code for 1.2.3 unless a field test finds something.
- **Branch: `Version_1.2.3`**, pushed through 9c08b2d; 2210817 (item 31's popup font fix and the hand-check notes)
  may not be pushed yet: check `git status -sb`. Never create branches; commit and push only when Greg asks.
- **`<Version>` is 1.2.3.1** (a field-test number) in `src/TermPoint/TermPoint.csproj`. The 1.2.3.1 build on the test
  computers predates most of the 2026-10-26 work, so **the first step is a new field-test build** (consider 1.2.3.2,
  so the window title and the log tell the builds apart). Before the Store/Velopack release build `<Version>` must go
  back to three numbers (the MSIX's fourth number must be 0; Velopack wants three parts); consider 1.2.4 so field-test
  machines see the release as an update.
- **Sources of truth:**
  - `docs/testing/network-field-tests-1.2.3.md`: **the field tests** (M = Must, S = Should, N = Nice), a status table
    at the top, open issues, each scenario with its expected results and a result line, and a suggested running
    order (blocks). Updated 2026-10-27 for the finished work (S3, S4, N3 rewritten; a new-build reminder).
  - `spec_version_1.2.3.md`: the items table and "Work remaining" (right after the table); each item has its own
    section with what was built and how it was tested.
  - `spec_version_1.2.4.md`: everything moved out of 1.2.3, plus what the 1.2.3 work turned up.
  - `designs/data-persistence-review-2026-10.md`: persistence findings F1–F16, the 1.2.4 redesign, the test harness.

## What is in the build (for reading field-test results)

Before 2026-10-26: items 1–5, 8, 9, 12, 16–24, 10a, F1/F4/F8/F12. On 2026-10-26/27:
- **27** (9d7c3bb) virtualization: adding a meeting keeps the open editor in view.
- **28** (a30df88) the message pass: the kept-changes promises (F6), the lock-file message (F13), one sticky "can't reach
  the database" notice that clears only when the location responds (F15), the Refresh messages, the cloud-folder
  warning. The approved texts are in spec section 28.
- **15, exit guard** (1587c22): repeated X ignored during the exit save; a "Saving and closing…" screen blocks input;
  2-minute backstop. DEBUG switch `TERMPOINT_DEBUG_SLOW_EXIT_SAVE=<seconds>`.
- **30** (74011a0) the partial-backup delete after a stalled copy has a network deadline.
- **13** (21d6623) SharePoint/Teams libraries synced through OneDrive get the cloud-folder warning (Windows, warn only),
  read from OneDrive's registry; **never yet seen on a real library**: S9 is its first check and holds a read-only
  PowerShell probe to run if no warning appears.
- **25** (fa2c5ee) Preferences: section-card conflicts "In full" or "As a flag" (door / person icon, list on hover).
- **31** (63e2af4, 2210817) the grid's Tags filter matches all (default) or any of the checked tags.
- **6** (9c37dce, 9e77278, 88cafdf) every BugSnag report is scrubbed (paths become tags such as `<drive H: path>`,
  known user and computer names `<user>` / `<machine>`, no host name, lock holders as `self` / `other` …), carries the
  install ID, and PRIVACY.md says so.
- **7** (9c08b2d) handled problems reach BugSnag once per run (refused save, lost lock, refused restore, torn transfer,
  save timeout, copy mismatch, failed Refresh, backup failures); a "running" file
  (`%APPDATA%\TermPoint\running\<pid>.run`) reports a previous session that ended without exiting normally; a failed
  backup shows one plain banner per run, without the folder's path.
- **Moved to 1.2.4** (not in 1.2.3; don't record their absence as failures): item 29 / F5 (skip no-edit saves; so the
  harness and field scenario M5 (4) can still show "Can't Restore" after a restore, a new database, a 1.2.2 direct open
  or the SQLite update), item 26 (a real Save As command), item 14 / S5, item 11's reconnection, item 13's other
  providers, macOS and a File → Open warning, the F2 edit counter.
- Full suite at 9c08b2d: **3,955 passed, 0 failed, 81 skipped** (harness and local-DB skips).

## Next

1. **A new field-test build** on GregDeskTop and GregSurface (see above).
2. **The field tests**, Must tier first: M4, M5 (with the three-computer test), M6, M7, M8, M9, M11 (b)–(d), and a quick
   re-run of M10. Then Should: S2, S3, S4, S7, S8, S9, S10, S11; Nice: N1, N2, N3. Record each result in its scenario
   and the status table. A failure that needs a code change goes through the usual workflow below.
3. **Release housekeeping:** `<Version>` back to three numbers, the Avalonia 12.1.4 decision (release notes, #22394),
   Store certification, then N4.

## Working agreements (Greg's)

- **Opus orchestrates, Sonnet codes.** Opus diagnoses and writes a self-contained brief; Sonnet implements (no
  commits, no branches); Opus reviews the diff, re-runs both compile checks and the full suite; Greg hand-checks;
  commit and push **only when Greg asks**. Never create branches. (Very small changes have been made directly.)
- **Small briefs, one kind of change each** (Greg asked for this after item 28's bundled brief took about an hour).
- **User-facing wording is approved by Greg** before coding. Plain words: what happened, whether work is safe, what
  to do; no paths, raw exceptions, "working copy", "local copy" or "safe".
- **Warn before editing any `.axaml` file** and ask Greg to close it in Visual Studio first.
- **Persistence work** (save, lock, exit, switch, restore, recovery) is checked against the four rules in `CLAUDE.md`
  before the layout; ask the `MinimumVersionToWrite` question for every release (1.2.3: No). Late in a release,
  changes inside the save code whose gain is mainly speed or a better recovery chance go to the next release, and the
  risk review comes with the design (item 29).
- **Section cards:** no new per-card controls; prefer a compact flag with the detail on hover (item 25).
- **Debugging:** with competing hypotheses, propose the cheapest probe and ask Greg to run it before theorising.

## This machine (dev computer) — practical notes

- **C: is nearly full** (about 2 GB free). Greg often has TermPoint running from `bin\Debug`, which locks the normal
  build output. Run tests with **`tools/run-tests-lowdisk.sh`** (hard-linked temp build, a free-space watchdog that
  kills only its own PID, cleans up after itself); see its header for usage. Full suite about 3–4 minutes; the
  persistence harness only runs with `TERMPOINT_HARNESS=1` (about 20 minutes). Delete `$TEMP` test logs and `.trx`
  files afterwards.
- **Compile check:** `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`, and the
  same with `-f net10.0-browser`. `-t:Compile` does not compile AXAML; the test runner's full build does.
- **Never kill processes by image name** (`taskkill /IM`); only by PID, and only your own.
- `python` here is Inkscape's (no pip): use **`py -3.11`** (openpyxl 3.1.5 installed) for
  `tools/section-import/make_template.py`.
- **Controlled Folder Access** blocks test runners from writing into `Documents`; the item 24 hand-test kit lives in
  `C:\Users\gregs\TermPoint import stress kit` (private data; never commit it).
- `src/TermPoint/Properties/launchSettings.json` is local (untracked): it sets `TERMPOINT_DEBUG_DATE`; add
  `TERMPOINT_DEBUG_SLOW_EXIT_SAVE` (seconds) to test the exit guard.
- TermPoint's log: `%APPDATA%\TermPoint\Logs\app-YYYY-MM-DD.log`. Running files: `%APPDATA%\TermPoint\running\`.
- `tools/ScrollAnchorProbe` re-checks the section list's scroll anchoring (`dotnet run --project tools/ScrollAnchorProbe`,
  add the hard-link properties from the test runner to save disk); re-run after every Avalonia upgrade and any change
  to `ListScrollAnchorBehavior`.
- Background Sonnet agents can die silently on usage limits: if one seems stuck, check `ListAgents`, file times and
  `dotnet` processes before assuming.
- `grep.exe.stackdump` in the repo root is untracked junk from a crashed grep; leave it or delete it, never commit it.
