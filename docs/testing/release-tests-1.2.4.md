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

- [x] **`MinimumVersionToWrite` question:** "Could the new version save anything the old version wouldn't have?"
      Ask it for the final list of 1.2.4 items. **No** (Greg, 2026-11-12, over the closed list: nothing new is
      saved in the database).
- [x] Version in `src/TermPoint/TermPoint.csproj` is the release number.
- [x] Full suite with the harness green (about 35 min), `.trx` kept. (2026-11-12 on `92b162d`: 4733 passed,
      0 failed, 2 skipped, 31 min; `E:\sa-rel124.trx`.)
- [x] Both compile checks (`-f net10.0` and `-f net10.0-browser`), 2026-11-12.
- [x] If Avalonia was upgraded: `dotnet run --project tools/ScrollAnchorProbe`. (Not upgraded in 1.2.4.)
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
| **M11**, restore from backup | the restart after a restore | 1.2.3 doc, M11 (a)–(d) | restored data shows; the old process is gone and the restored database reopens (summary 4.3, fixed in 1.2.4) |
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

- [ ] **Package contents** (summary 1.2, 1.4): `build-msix.ps1 -Store` prints "Leaving out:" for
  `libHarfBuzzSharp.pdb`, `libSkiaSharp.pdb` and `Microsoft.DiaSymReader.Native.amd64.dll` (1.2.4 build:
  55.5 MB). On the sideload, a logged error still shows its stack trace (method names; no line numbers in Store
  builds, as before).
- [ ] **Package flight** (summary 1.3) before the public release. Every Store release from now on:
  1. Once only: Partner Center → Engage → Customers → **Known user groups**: a group "TermPoint testers" holding
     the Microsoft accounts signed in to the Store on GregDeskTop and GregSurface.
  2. Partner Center → TermPoint → **Package flights → New package flight**: name it for the version (e.g.
     "1.2.4"), pick "TermPoint testers", upload the Store `TermPoint.msix`, submit. It goes through certification
     like a public submission.
  3. On a computer that has the **previous Store version** installed and has opened it at least once: Microsoft
     Store → Library → Get updates (or wait). Leave other apps running as normal (Phone Link was the trigger in 1.1).
  4. Click TermPoint in the Start menu. **Pass:** it opens straight away, the version in Help is the new one, the
     install channel is "Store", and it opens a copy of the test database. If it doesn't open: note it, restart,
     check again, and add it to the support case (summary 1.1).
  5. Only then create the public submission with the same `TermPoint.msix`, set mandatory if
     `MinimumVersionToWrite` was raised, and submit.
  - For 1.2.4 the 1.2.3 → 1.2.4 update can still hit 1.1 once (the 1.2.3 package carries the DLL); from
    1.2.4 → 1.2.5 on it shouldn't.
- [ ] After certification: the Store update from 1.2.3 opens without a restart (summary 1.1; the support case).
- [ ] N4, the Store update link (1.2.3 doc).

## Log

- 2026-11-08: list written. Already passed this week: M5 at ×3 (2026-11-07), M6 by Hibernate on GregSurface
  (2026-11-08), the ×3 / normal-timings guards both ways (2026-11-07 and 2026-11-08).
- 2026-11-12: Part 1 gates passed (MinimumVersionToWrite No, Greg; full harness suite 4733/0/2; both compile
  checks). Release-candidate sideload MSIX 1.2.4.0 built from `92b162d`:
  `\\mekler\Users\gregs\temp\msix-1.2.4.0-rc1\` (hash `4619425AAE04…`); going on GregDeskTop and TPVM1 (TPVM1 via
  1.2.3.4 for the MSIX upgrade check).
- 2026-11-12: RC installed on GregDeskTop (timings off) and TPVM1 (×3). **MSIX upgrade (Part 4) passed, first
  half:** on TPVM1, 1.2.3.4 with a kept unsaved edit (hard stop), then the RC installed over it: title "(Test
  timings ×3)", settings and recent databases kept, "Restore Unsaved Changes?" offered and the edit came back.
  `Ready` retaken on the RC. Left: the fresh install on TPVM1.
- 2026-11-12: **M4 passed** on the RC (GregDeskTop normal timings, TPVM1 ×3). B1: GregDeskTop writer, clumsy Lag 110 +
  Drop 2 %, about 6 min; TPVM1 never offered write access. 1 of 3 saves landed; 2 gave up on the 5 s no-progress
  deadline (upload, then the uploaded copy's check), one plain message each; the same 5 s deadlines are in 1.2.3, so
  this is the link, not a change. Their edits landed at the next save. The two abandoned upload files
  (`BIOL-TT.db.<8 hex>.tmp`) were swept when TPVM1 took write access in B2. B2: GregDeskTop closed; TPVM1 offered the
  takeover, saved `ZZZT-B2`; GregDeskTop reopened read-only. B3: TPVM1 killed (Task Manager) with `ZZZT-B3V` unsaved;
  GregDeskTop took over and saved `ZZZT-B3D`; TPVM1's restart offered Restore, then "Unsaved Changes Kept",
  read-only. D (integrity ok, 343 sections) has B1a-c, B2, B3D; TPVM1's kept copy has B3V with its dirty marker.
  Rig notes: the Default Switch trap (a checkpoint older than the host's restart has no network; README updated);
  Mekler briefly refused the share with "account locked out" (cause unknown; cleared by itself).
- 2026-11-12: **M5 (1)-(3) passed** on the RC (TPVM1 paused as the sleeper, ×3; GregDeskTop the other user). (3)'s
  "taken over" banner came 5-10 s after the wake (1.2.3: 8 minutes, A8). **M5 (4) found a gap** (twice): GregDeskTop
  took over, made no edit and closed, which deletes the lock file; TPVM1 woke to no lock file, treated it as removed by
  another program, put it back and stayed writer; its save went through (safe: the database was as it last saw it).
  The banner blames antivirus or cloud sync (wrong here, rule 4), and the test's expectation ("Restore Unsaved
  Changes?") was 1.2.3's. Five harness proof tests now pin the route, including the dangerous variant (the other person
  saves, then closes: the sleeper's save is refused), all passing: `Harness/TakeoverThenReleaseProofTests.cs`. Review and
  recommendations: `designs/harness-review-2026-11.md`. Left: M5 (4b) by hand, M5 (5).
- 2026-11-12: **Code change after the RC (Greg): the "lock put back" notice is dropped.** A missing lock file is put
  back silently (log line and breadcrumb kept); `LockLossPrompt.LockFilePutBack` removed. Display only: when and what
  TermPoint writes is unchanged; MinimumVersionToWrite still No. Both compile checks pass; the affected tests (397,
  with the harness) pass; full suite with the harness: 4737 passed, 0 failed, 2 skipped (`E:\sa-rel124b.trx`, 30 min).
  Two more proof tests added after that run (Alice still open: Bob's save refused, or his wake makes him read-only),
  both passing; 7 takeover routes now pinned. **Consequences:** a new RC
  sideload is needed (the rig's RC still shows the notice); M5 (4)'s expectation is now "no message; Bob stays writer;
  his save lands" when the other person changed nothing, and M5 (4b) "Bob's first save is refused with the conflict
  banner, the only message" when they saved. M4 and M5 (1)-(3) don't touch this path and stand.
