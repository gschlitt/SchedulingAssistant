# TermPoint 1.2.4 — release test list

The hand tests for releasing 1.2.4 (spec item 8.11). It replaces the 1.2.3 checklist's long Must tier with what the
harness can't simulate. Step-by-step procedures stay in
[network-field-tests-1.2.3.md](network-field-tests-1.2.3.md): this list says **which** of them to run, and adds what's
new in 1.2.4.

Written 2026-11-08, while 1.2.4 is still being built. **Fixes still to come (F20, F17, F18, F23, 6.12 and others) each
add their own field check here, with the fix.**

## How to use it

- **Rig:** the `tools/FieldTest` module (its `README.md`), GregDeskTop + TPVM1, the test database on Mekler.
  `Restore-TestComputer -Machine TPVM1` gives 1.2.4 at test timings ×3 (lock timers 3× faster).
- **Test timings** are for lock-timer scenarios (takeover, stale lock, heartbeat). Run anything involving install,
  upgrade, the Store, sleep or real-world timing at **normal timings** (`Set-TestTimings -Scale 1`).
- **Order:** Part 1 (gates) any time; Parts 2–4 on the release candidate; Part 5 after Store certification.
- Record results in the Log at the end, one line each.

## What the harness now covers (no hand run)

These 1.2.3 tests re-checked logic the harness tests on every full run (`TERMPOINT_HARNESS=1`). Each one's on-screen
messages are still seen once, in the wording pass (Part 3).

| 1.2.3 test | Covered by |
|---|---|
| M1 Part B, network cut mid-save | network down or hung at each step of a save; crash points |
| M2, kill the writer | crash then relaunch, swept over every crash point |
| M3, X and Discard on "Restore Unsaved Changes?" | every answer scripted, including the X; rule F1 |
| M9, a colleague joins during an edit | a second computer opens while the first edits |
| M10, exit save on a slow or dead link | network hang during close (`NetworkHangTests`) |
| M12, the database changed by someone else | colleague writes / old file restored, then save, then both conflict-banner choices |
| S2, local copy fails after validation | a pause drops the network at that exact point (A13) |
| S1, second copy on the same computer | `DatabaseInstanceGuardTests` (unit tests; not acted out by the harness) |
| S6, new database / export with the share down | unit tests (`NewDatabaseFileTests` and others); the probe covered (b) in 1.2.3 |

Partly covered, so still in Part 2 for what the harness can't do: M4, M5, M6, M7, M11 (crash sweep only), S3 (crash
sweep only), S4.

## Part 1 — Release gates

- [ ] **`MinimumVersionToWrite` question:** "Could the new version save anything the old version wouldn't have?"
      Ask it for the final list of 1.2.4 items. So far **No** (colour by subject, collapse days and the editor
      conflict notice write nothing to the database).
- [ ] Version in `src/TermPoint/TermPoint.csproj` is the release number.
- [ ] Full suite with the harness green (about 35 min), `.trx` kept.
- [ ] Both compile checks (`-f net10.0` and `-f net10.0-browser`).
- [ ] If Avalonia was upgraded: `dotnet run --project tools/ScrollAnchorProbe`.
- [ ] Package checks done or deliberately deferred: summary items 1.2 (debugging DLL), 1.4 (debug files), 1.5
      (GitHub Tests workflow).

## Part 2 — Field tests on the rig (real share)

| Test | Why it stays | How | Pass when |
|---|---|---|---|
| **M1 Part A**, real network cut mid-save | real SMB behaviour | `tools/NetworkProbe` from GregDeskTop (its README); one cut in TermPoint with clumsy | probe all green; TermPoint keeps the work and shows one message |
| **M4**, two-user contention | real SMB locking | 1.2.3 doc, M4 (B1–B3), GregDeskTop + TPVM1 | as in the 1.2.3 doc |
| **M5**, stale-lock takeover | real timers across two computers | 1.2.3 doc, M5 (1)–(5), at ×3 | passed 2026-11-07 at ×3; re-run on the release candidate |
| **M5 wait wording** (4.12) | new in 1.2.4 | during a takeover wait at normal timings, watch the dialog | it says "(up to 3 minutes)" from 90 s on, and the wait ends by 170 s |
| **M6**, sleep mid-save | real sleep and SMB caching | GregSurface, normal timings: save, then **Hibernate** at once; take the lock from GregDeskTop; wake after 4 min | no "lock confirmed" in the log after the wake; one lock-lost message; the work is kept (passed 2026-11-08) |
| **M7**, an editor open across a lost lock | the open editor on screen | 1.2.3 doc, M7 | nothing written after the loss; the editor's changes are kept |
| **M8**, a real older version alongside | real older builds | 1.2.3 doc, M8, with the **1.2.3 Store build** on one computer and 1.2.4 on the other (both directions) | as in the 1.2.3 doc; no read-only gate (MinimumVersionToWrite still No) |
| **M11**, restore from backup | the restart after a restore | 1.2.3 doc, M11 (a)–(d) | restored data shows; note whether the old process is left running (summary 4.3, still open) |
| **S3**, reader Refresh with the share down | network faults during Refresh aren't in the harness | 1.2.3 doc, S3 | one plain message; the read-only view still works |
| **S4**, repeated X during the exit save | window behaviour | 1.2.3 doc, S4, plus a detached panel open | one save, one message, nothing lost |

## Part 3 — One on-screen wording pass

The harness checks that each problem gives one message and that no message uses banned words, but it can't see the
screen. Look at each of these once, on the release candidate, and check the wording reads well and the dialog is in
front:

- [ ] "Restore Unsaved Changes?" with Restore, Discard (then "Discard Unsaved Changes?") and the X (M3).
- [ ] The save conflict banner, "Save a Copy…" and "Discard Changes" (M12).
- [ ] Lock lost (taken over), as a banner in the main window and with a detached panel open.
- [ ] The outage notice during a save with the share down (M1).
- [ ] The stale-lock takeover dialog and its wait (M5).
- [ ] Read-only mode's banner and "write access available".
- [ ] The second-copy message (S1) and the new-database-with-share-down message (S6).

## Part 4 — Platforms and features

| Check | How | Pass when |
|---|---|---|
| **MSIX upgrade** | install the 1.2.4 sideload over 1.2.3.4; then on TPVM1, uninstall TermPoint and install 1.2.4 fresh (restore `Ready` afterwards) | settings, recent databases and kept changes survive the upgrade; a fresh install starts the wizard |
| **S7** wizard and UNC picker | 1.2.3 doc, S7, on Windows 10 (GregDeskTop) and Windows 11 (TPVM1) | the share opens from both |
| **S8** Windows 11 AutoCompleteBox crash test | 1.2.3 doc, S8, on TPVM1 | no crash |
| **S9** cloud-synced folders | 1.2.3 doc, S9 | the warning shows for a OneDrive and a SharePoint/Teams folder |
| **S10** section list on a large database | 1.2.3 doc, S10 | as in the 1.2.3 doc |
| **S11 macOS** | signed build from a `v*` tag; 1.2.3 doc, S11, plus the 1.2.4 features below | as in the 1.2.3 doc; folded days and subject colours look right |
| **Colour by subject** (2.3) | real data, colours on / when more than one subject / off | Greg's eye pass; tiles and cards agree |
| **Instructor lists on tiles** (2.5) | a section with many instructors | the list is shortened as designed |
| **Collapse days** (2.13) | fold, Show only, block patterns, Show all; PNG export | as `designs/collapse-days.md`; note the two flash nits (stepping through Room Browser slots flashes each step; a redraw within a second cuts a flash short) |
| **Subjects in the Schedule View title** (2.14) | 7 vs 9+ subjects, colours on and off, a narrow detached window, its title bar | chips wrap between chips; "+N" shows the rest on hover |
| **Conflicts in the section editor** (2.19) | change an instructor or a meeting into a clash | the notice shows before saving (Greg's eye check passed 2026-11-06) |

## Part 5 — Store

- [ ] Package flight (summary 1.3) before the public release.
- [ ] After certification: the Store update from 1.2.3 opens without a restart (summary 1.1; the support case).
- [ ] N4, the Store update link (1.2.3 doc).

## Log

- 2026-11-08: list written. Already passed this week: M5 at ×3 (2026-11-07), M6 by Hibernate on GregSurface
  (2026-11-08), the ×3 / normal-timings guards both ways (2026-11-07 and 2026-11-08).
