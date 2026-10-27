# Handoff: TermPoint 1.2.3, 2026-10-26 (dev machine)

Written at the end of a long session so a fresh conversation can pick up. Claude Code's memory and `CLAUDE.md` don't
travel between machines (`CLAUDE.md` is gitignored), so everything a new agent needs is here or in the files this
points to.

**To start the new agent, say:** "Read `HANDOFF.md`, then continue 1.2.3 with item 6."

## Where things are

- **Branch: `Version_1.2.3`.** It now holds everything for 1.2.3. On 2026-10-26 `1.2.3_new_features` (items 20 and
  24) was merged in (merge 7ecef94, verified first) and `Version_1.2.3` fast-forwarded; `1.2.3_new_features` is
  finished. Check `git status -sb` for what is pushed (item 15's commit 1587c22 may not be).
- **`<Version>` is 1.2.3.1** (a field-test build number). Before the Store/Velopack release build it must go back to
  three numbers (the MSIX's fourth number must be 0; Velopack wants three parts); consider 1.2.4 so 1.2.3.1 test
  machines see the release as an update.
- **Sources of truth:**
  - `spec_version_1.2.3.md`: the items table, **"Work remaining"** (right after the table) and the implementation
    order (**Phase 10** = what is left to build, in order). Each item has its own section.
  - `spec_version_1.2.4.md`: everything moved out of 1.2.3 (the scope review of 2026-10-26).
  - `docs/testing/network-field-tests-1.2.3.md`: the field tests (M, S, N scenarios) with their status.
  - `designs/data-persistence-review-2026-10.md`: persistence findings F1–F16 (section 3), the 1.2.4 redesign
    (section 8), the test harness (section 9).

## Done (all on `Version_1.2.3`)

- Items 1–5, 8, 9, 12, 16–24 and 10a, F1/F4/F8/F12 (before this session).
- Item 24 (Section Import by User) complete, including stress tests, five importer fixes and retiring the Dev Tools
  CSV import (24.10). `MinimumVersionToWrite`: No, confirmed by a 1.2.2 round trip.
- **2026-10-26:** the scope review (every unbuilt item decided by Greg, one at a time), then:
  - **item 27** (9d7c3bb): with virtualization on, adding a meeting no longer pushes the open editor out of view
    (the anchoring now rebuilds a pinned card the panel virtualized away; `ScrollAnchorProbe` E9);
  - **item 28** (a30df88): the message pass (F6 kept-changes promises, F13 lock file, F15 one sticky outage notice,
    item 11's Refresh messages, item 13's cloud-folder warning);
  - **item 15, 1.2.3 part** (1587c22): the exit guard (repeated X ignored while saving and closing, "Saving and
    closing…" curtain, secondary windows hidden, 2-minute backstop, DEBUG switch `TERMPOINT_DEBUG_SLOW_EXIT_SAVE`).
- Full suite at 1587c22: **3,868 passed, 0 failed, 81 skipped** (harness and local-DB skips).

## Next: the rest of Phase 10, in this order

1. ~~**Item 29, F5**~~ **moved to 1.2.4** (Greg, 2026-10-26), after its design, probe and risk review: a risky change
   inside the save code for what is mainly a performance gain and a better chance of restoring after a crash. 1.2.3
   ships with no change inside the save code. The design (with the risk review's additions) is kept in
   `spec_version_1.2.3.md` section 29 and listed in `spec_version_1.2.4.md`. So the harness keeps reporting F5 and
   F6's F5-shaped route as known.
2. ~~**Item 30**~~ **done 2026-10-26:** the partial-backup delete after a stalled copy goes through
   `NetworkFileOps.DeleteAsync`; `BackupServiceStalledCopyTests` (2). See spec section 30.
3. ~~**Item 13**~~ **done 2026-10-26 (21d6623):** SharePoint/Teams libraries synced through OneDrive are detected
   from OneDrive's registry entries (`FolderAssessor`), warn only, label "OneDrive (a SharePoint or Teams library)"
   (approved). Not yet seen on a real library: field test S9 confirms it (probe included there). File → Open warning
   moved to 1.2.4.
4. ~~**Item 25**~~ **built 2026-10-26 (hand check pending):** reframed by Greg as a vertical-space fix.
   One Preferences choice shows section-card conflicts "In full" (default) or "As a flag" (door / person icon +
   "Room conflict" / "Instructor conflict", full list on hover). See spec section 25 for the hand check.
5. ~~**Item 26**~~ **moved to 1.2.4** (Greg, 2026-10-26): instead of a Help note explaining how to copy the database
   file by hand, 1.2.4 gets a real Save As / Save a Copy… command. Findings in spec item 26.
6. **Item 6:** BugSnag scrubber, steps 6a–6c as planned in its section. **6a done 2026-10-26:**
   `Services/PrivacyScrubber.cs` (`ScrubText`, `RegisterSensitive`), used by `DatabaseDamageReporter.Sanitize`.
   **6b done 2026-10-26:** `Services/ScrubbingDelivery.cs`, every BugSnag payload goes through it
   (`App.CreateBugsnagClient`); it also stamps the install ID. Next: 6c.
   **Item 31 (added 2026-10-26) built (hand check pending):** the grid's Tags filter can match all
   (default) or any of the checked tags, chosen at the top of the Tags popup. See spec section 31.
7. **Item 7:** handled-failure and unclean-exit reports to BugSnag, after item 6.

**Then the field tests.** The remaining Musts (M4, M5, M6, M7, M8, M9, M11 (b)–(d)) are best run once items 15 and
30 have landed, so they test the shipping close/save code; plus S3 (Refresh messages), S4 (exit guard), S9
(SharePoint/Teams), N3 (BugSnag), and a quick re-run of M10. **Then release housekeeping:** `<Version>`, the Avalonia
12.1.4 decision, Store certification then N4.

## Working agreements (Greg's)

- **Opus orchestrates, Sonnet codes.** Opus diagnoses and writes a self-contained brief; Sonnet implements (no
  commits, no branches); Opus reviews the diff, re-runs both compile checks and the full suite; Greg hand-checks;
  commit and push **only when Greg asks**. Never create branches.
- **Small briefs, one kind of change each** (Greg asked for this after item 28's bundled brief took about an hour).
- **User-facing wording is approved by Greg** before coding. Plain words: what happened, whether work is safe, what
  to do; no paths, raw exceptions, "working copy", "local copy" or "safe".
- **Warn before editing any `.axaml` file** and ask Greg to close it in Visual Studio first.
- **Persistence work** (save, lock, exit, switch, restore, recovery) is checked against the four rules in `CLAUDE.md`
  before the layout; ask the `MinimumVersionToWrite` question for every release (1.2.3's features so far: No).
- **Debugging:** with competing hypotheses, propose the cheapest probe and ask Greg to run it before theorising.

## This machine (dev computer) — practical notes

- **C: is nearly full** (about 2 GB free). Greg often has TermPoint running from `bin\Debug`, which locks the normal
  build output. Run tests with **`tools/run-tests-lowdisk.sh`** (hard-linked temp build, a free-space watchdog that
  kills only its own PID, cleans up after itself); see its header for usage. Full suite about 3–4 minutes; the
  persistence harness only runs with `TERMPOINT_HARNESS=1` (about 20 minutes).
- **Compile check:** `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`, and the
  same with `-f net10.0-browser`.
- **Never kill processes by image name** (`taskkill /IM`); only by PID, and only your own.
- `python` here is Inkscape's (no pip): use **`py -3.11`** (openpyxl 3.1.5 installed) for
  `tools/section-import/make_template.py`.
- **Controlled Folder Access** blocks test runners from writing into `Documents`; the item 24 hand-test kit lives in
  `C:\Users\gregs\TermPoint import stress kit` (private data; never commit it).
- `src/TermPoint/Properties/launchSettings.json` is local (untracked): it sets `TERMPOINT_DEBUG_DATE`; add
  `TERMPOINT_DEBUG_SLOW_EXIT_SAVE` (seconds) to test the exit guard.
- TermPoint's log: `%APPDATA%\TermPoint\Logs\app-YYYY-MM-DD.log`.
- `tools/ScrollAnchorProbe` re-checks the section list's scroll anchoring (`dotnet run --project tools/ScrollAnchorProbe`,
  add the hard-link properties from the test runner to save disk); re-run after every Avalonia upgrade and any change
  to `ListScrollAnchorBehavior`.
- Background Sonnet agents can die silently on usage limits: if one seems stuck, check `ListAgents`, file times and
  `dotnet` processes before assuming.
