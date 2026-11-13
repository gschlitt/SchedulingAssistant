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
| **M5**, stale-lock takeover | real timers across two computers | 1.2.3 doc, M5 (1)–(5), at ×3 | passed 2026-11-07 at ×3; re-run on the release candidate. **(4) changed for 1.2.4:** the other person takes over, changes nothing and closes; the sleeper wakes: no message, it stays the writer, its save lands |
| **M5 (4b)**, new in 1.2.4 | the dangerous variant of (4) | as (4), but the other person adds a section and saves before closing | no message at the wake; the sleeper's first save (or autosave) is refused with the conflict banner, the only message; D has the other person's section only; the sleeper's section is in its kept copy |
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
- 2026-11-12 (evening): **RC2 built** from `afd863f` (= RC1 + `6e134ea`, the dropped notice; `afd863f` itself is
  test-only): `\\mekler\Users\gregs\temp\msix-1.2.4.0-rc2\` (with `BUILD.txt`), installed on TPVM1 (×3, `Ready` retaken)
  and GregDeskTop (normal timings). Lock-scenario review the same day (`tmp/network_scenarios/lock-scenarios-review.md`,
  local only): no data-loss gap; two availability findings pinned as tests for 1.2.5.
- 2026-11-12 (evening): **M5 (4) passed on RC2** (TPVM1 sleeper with unsaved ZZZT-4V, paused 300 s; GregDeskTop took
  over, no edit, closed): no message on waking, TPVM1 stayed writer, its save landed (ZZZT-4V in D). GregDeskTop took
  over with no wait, as designed: it had opened read-only at 19:47:01 just as the freeze began, its poll offered the lock
  at 19:48:02, and at the click (19:48:43) it had watched the heartbeat stand still for 101 s, more than the ×3
  confirmation (~57 s). **M5 (4b) passed on RC2**: GregDeskTop took over, saved ZZZT-4bA, closed; TPVM1 woke with no
  message; its save was refused with the conflict banner, the only message; D has ZZZT-4bA, not ZZZT-4bV.
  Left in Part 2: M5 (5), M5 wait wording, M1 Part A, M7, M8, M11, S3, S4.
- 2026-11-12 (evening): **M5 (5) passed on RC2** (TPVM1 sleeper at ×3 with unsaved ZZZT-5V, paused 240 s; GregDeskTop
  reader): after TPVM1 exited cleanly, GregDeskTop's "Take over as writer" appeared within ~60 s and worked; ZZZT-5V in D.
  **Seen on the way (safe; rule 4, moved to 1.2.5 as S38):** at ×3 the frozen lock is stale after 60 s, so GregDeskTop,
  read-only from 20:08:09, was offered the lock at 20:09:09 while TPVM1 was still frozen (correct). After TPVM1 resumed
  the offer stayed on screen (polling stops once it offers); clicking it at 20:12:38 re-checked, found TPVM1 live and
  went back to the read-only banner with no message. (1.2.3's run at normal timings never reached an offer before the
  sleeper woke, so this wasn't seen then.) Left in Part 2: M5 wait wording, M1 Part A, M7, M8, M11, S3, S4.
- 2026-11-12 (evening): **M5 wait wording passed on RC2** (normal timings on both computers; GregDeskTop opened into
  "Database Lock Is Inactive" and took over at once): the wait dialog showed "Checking the lock (up to 3 minutes)" and
  closed when the wait ended, as expected. Left in Part 2: M1 Part A, M7, M8, M11, S3, S4.
- 2026-11-13: **M1 Part A, probe half, passed** (`tools/NetworkProbe` rebuilt on afd863f's `NetworkFileOps`, plus two
  new cases for the readers' copy-and-hash of 1.2.4 items 5.3/5.5, `copy-hash-cb` and `copy-hash-cm`): 25 of 25
  passed, both controls OK, no call-return over 0.31 s. Cut-before cases gave up at 5.0 s (integrity 5.3 s);
  `copy-cm` caught in `CopyFile (write)` at 1.8 s, `hash-cm` in `ComputeHash (read)`, `copy-hash-cm` in
  `CopyFile (read)` at 2.1–2.4 s. Report `tmp\netprobe\netprobe-20261113-075536.md`. Left in M1 Part A: the one
  clumsy cut in TermPoint.
- 2026-11-13: **M1 Part A, TermPoint cut, passed on RC2** (GregDeskTop writer, autosave off, clumsy Lag 110 then Drop
  100 at about 09:12 during the save of `ZZZT-M1C`): Greg saw the outage notice as expected; log: `File.Stat timed out`
  09:12:54 (the save's stat of D gave up), heartbeat renewal timed out 09:13:47, the next save landed 09:14:03, then
  "Save recovered after failing". **Moved to 1.2.5 (Greg; backlog item M1A):** the notice ends "Press Save once the connection is
  back." but nothing tells the user when that is (the notice clears silently when the heartbeat reaches the share, up
  to ~60 s after the network returns). Left in Part 2: M7, M8, M11, S3, S4.
- 2026-11-13: **M7 passed on RC2** (GregDeskTop, local copy `tmp\m7-124\M7-test.db`, autosave off, Alice via
  `tmp\handtest_lock.py`): the heartbeat found Alice's lock (09:19:26, `NotOurs`, demoted in place with the takeover
  banner, Alice's lock left alone); the course editor stayed open; its Apply was refused once (09:19:40) with the
  plain sentence; clean exit; the change is not in the copy. The refusal shows in the app's error banner, which is
  red: the 1.2.3 doc's "no red banner" meant no raw log text, which there wasn't. Left in Part 2: M8, M11, S3, S4.
- 2026-11-13: **M8 passed** (GregDeskTop RC2, TPVM1 Store 1.2.3.4 beside its RC2; on TPVM1 the packaged builds keep
  their data in their own package folders, so the two never shared settings or kept changes). D reset, then
  `zzFutureField` = `keep-me` planted on Avril Alfred (`tmp\m8-124\BIOL-TT-m8-planted.db`). Dir 1: RC2 edited Avril
  ("Alfred!") and saved `ZZZT-M8A` (09:44:03); 1.2.3 read-only, took over, saved `ZZZT-M8B`; RC2 reopened cleanly.
  Dir 2: 1.2.3 writer; RC2 read-only naming TPVM1 (09:47:54), "lock file gone" 09:48:54, took over 09:49:01, saved
  `ZZZT-M8D` (09:49:32). Dir 3: RC2 writer (09:49:45) with `ZZZT-M8E` unsaved, 1.2.3 joined read-only, RC2's save
  landed with no conflict (09:50:55): a 1.2.3 reader leaves D alone. A copy of D (Claude): integrity ok, all five
  sections, the planted field still `keep-me` through both versions' edits, no `MinimumVersionToWrite` row (every
  RC2 open logged "stamp none"). Left in Part 2: M11, S3, S4.
- 2026-11-13: **M11 (a), (c), (d) passed on RC2** (GregDeskTop writer from 09:54:45, TPVM1 restored to `Ready`).
  (a) clumsy Drop at ~09:56 during a Save & Backup restore: the expected notice; the cut caught the save before the
  restore (09:56:31 lock check timed out, "restore not started"), not the restore's own copy (about 600 KB, too quick
  to aim at by hand; this morning's NetworkProbe covers its copy and hash calls); the next save recovered 09:57:30.
  (c) TPVM1 read-only, Save & Backup offered no restore. (d) TPVM1's chooser refused the restore naming GregDeskTop.
  Through (a)–(d) D was not written after GregDeskTop's 09:55 save and the lock stayed GregDeskTop's session
  (heartbeat current at 10:08). Left in M11: (b).
- 2026-11-13: **M11 (b) passed on RC2; M11 done** (summary 4.3 confirmed in the field). GregDeskTop saved
  `ZZZT-M11B` (10:08:56), restored from Save & Backup (10:09:27 `DatabaseRestore: Restored`), released the lock;
  10:09:35 "The main window closed but the main loop is still running; asking for the shutdown again" (4.3's fix (A)
  fired: the old cause still happens, the fix now ends the process), Main returned; the relaunch logged "Started
  after a restore: reopening the restored database" and took write access (10:09:37). Greg: the expected message,
  the restored database opened directly, one TermPoint process (started 10:09:35, with its window). Copies (Claude):
  D holds an older backup (343 sections, `ZZZT-309 D`…`ZZZT-412 S`, none from today), integrity ok;
  `BIOL-TT (before restore 2026-11-13 10-09).db` holds today's work (`ZZZT-M8A`–`E`, `ZZZT-M11A`, `ZZZT-M11B`),
  integrity ok. Left in Part 2: S3, S4.
- 2026-11-13: **S3 passed on RC2** (TPVM1 writer at ×3, GregDeskTop reader). (a) clumsy Drop 100 on GregDeskTop,
  Refresh: "Couldn't refresh: the database location isn't responding…" (log `refresh copy timed out`,
  `SourceUnavailable`); network back, the next Refresh worked and cleared the line ("Refresh recovered after
  failing"). (b) the reader's copy `…_BIOL-TT_ro.db` held open ('Read', 'ReadWrite'; a first try never ran, the
  `Open` line was left unfinished at a `>>` prompt): 10:23:21 the swap's rename was refused (`ReplaceFailed`),
  Greg saw "Couldn't refresh just now… Try again in a moment.", the view stayed usable (4.6: no more "lost its
  connection" line); file released, 10:23:46 Refresh worked and showed the new section. No red log-text banner.
  Left in Part 2: S4.
- 2026-11-13: **S4 passed on RC2; Part 2 done** (GregDeskTop writer, a detached panel open, clumsy Lag 110):
  `ZZZT-S4A` unsaved, X then more X clicks during the exit save (Greg: the cover screen, input blocked, panel hidden,
  window closed when the save ended). Log: one "close request ignored — saving and closing is in progress"
  (10:27:14), one save (10:27:33), clean shutdown, lock released 10:27:34. A copy of D (Claude): `ZZZT-S4A` present,
  integrity ok. Not tried: the 2-minute backstop (Debug-build switch).
