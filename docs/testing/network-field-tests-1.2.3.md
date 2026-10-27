# TermPoint 1.2.3 — network field tests

Hand-run checklist, essential first. It re-runs the July protocol
([degraded-network-field-tests-2026-07-04.md](degraded-network-field-tests-2026-07-04.md): A, B1–B5, C1–C3) against the
1.2.3 release build and adds the 1.2.3 scenarios of the spec's "Release testing: network field tests"
(`spec_version_1.2.3.md`) and the fixes F1, F4, F8 and F12 ([review §3](../../designs/data-persistence-review-2026-10.md)).
**Do the Must tier before release; Should and Nice only after.** Every 1.2.3 item is now built (2026-10-26) and
committed on `Version_1.2.3`; scenarios for work moved to 1.2.4 (item 14 / S5, item 11's reconnection, item 13's
other providers and macOS) say so. **Test on a build made from `Version_1.2.3` at or after 2210817**: the 1.2.3.1
build on the test computers predates items 6, 7, 13, 15's exit guard, 25, 28, 30 and 31.

## Status (updated 2026-10-27)

Two parts. **Scenarios** lists every scenario in this checklist, one row each, with what it needs: the details and the
recorded results are in each scenario further down. **Open issues** lists what testing turned up that is not a
scenario: fixes, things to watch for, and reminders.

### Scenarios

"Needs": **one computer** = any single computer with TermPoint and a local database copy (the laptop on its own is
enough); **share** = the database on Mekler's share; **both** = GregDeskTop and GregSurface together.

| Scenario | Tier | Needs | Status |
|---|---|---|---|
| M1 Network cut mid-save | Must | one computer + share (Part A: probe on GregDeskTop) | ✅ pass (probe 18/18; three cuts in TermPoint) |
| M2 Kill the writer | Must | one computer + share | ✅ pass |
| M3 F1: X and Discard on "Restore?" | Must | one computer | ✅ pass |
| M4 Two-user contention | Must | both + share | ✅ pass (B1, B2, B3) |
| M5 Stale-lock takeover | Must | both + share (+ a third computer for (6)) | ◐ (1), (2), (4), (5) pass; (3) and (4) ✗ on waking (A8, twice; D safe); (6) not run; (7) to do |
| M6 F12: the laptop sleeps mid-save | Must | both + share | ✅ pass (window hit; final check refused; work kept); A8, A11 seen |
| M7 F4: an editor open across a lost lock | Must | one computer (Python + repo for the helper script) | ✅ pass (nothing written after the loss); found A2–A5 |
| M8 Mixed 1.2.2 and 1.2.3 | Must | both + share + a sideload 1.2.2 build | ☐ to do (block 5) |
| M9 A colleague joins during an edit | Must | both + share | ✅ pass |
| M10 Exit save on a slow or dead link | Must | one computer + share | ✅ pass (re-run on 1.2.3.2, 2026-10-27) |
| M11 Restore from backup | Must | (a) one computer + share; (b)–(d) both + share | ✅ pass ((a)–(d)); the restart after (b) left a stuck process (see Open issues) |
| M12 D changed by someone else | Must | one computer + share (Python + repo) | ✅ pass |
| S1 Second copy on the same computer | Should | one computer | ✅ pass |
| S2 Local copy fails after validation | Should | both + share | ☐ to do |
| S3 Reader Refresh failures | Should | one computer (the DEBUG switch) | ☐ to do (item 11's messages, built in item 28; the reconnection moved to 1.2.4) |
| S4 Repeated X / edits during exit save | Should | one computer + share | ✅ pass (with M10; detached panels and the 2-minute backstop not tried) |
| S5 Unapplied editor changes at close/switch | Should | one computer | — moved to 1.2.4 with item 14 |
| S6 New database / export with the share down | Should | one computer + share | ✅ pass ((b) covered by the probe) |
| S7 Wizard and UNC picker | Should | share; a Windows 10 and a Windows 11 computer | ☐ to do |
| S8 Windows 11 AutoCompleteBox crash test | Should | one computer on Windows 11 | ☐ to do |
| S9 Cloud-synced folders | Should | one computer with OneDrive and a SharePoint/Teams library | ☐ to do (item 13's SharePoint/Teams detection built 2026-10-26; this is its first check on a real library. macOS, other providers and a File → Open warning moved to 1.2.4) |
| S10 Section list: recycling test, default OFF | Should | one computer, ~800-section database | ☐ to do |
| S11 macOS pass | Should | Mac | ☐ to do |
| N1 Virtualization ON | Nice | one computer, ~800-section database | ☐ to do |
| N2 Touch and trackpad | Nice | a touchscreen (GregSurface); Mac | ☐ to do |
| N3 BugSnag | Nice | one computer | ☐ to do (items 6, 7 built 2026-10-26) |
| N4 Store update link | Nice | a computer still on 1.2.2 | ☐ after Store certification |

### Open issues

- [x] **A1 found and fixed** (b193eb8): a cut mid-copy could go unnoticed for the whole outage (no log line, no
      banner) because the copy's chunk write could block before its 5 s timer started; the hash's reads had the same
      pattern. Both now start under `Task.Run` (`NetworkFileOps`). Probe before: 1 of 3 mid-copy cuts failed; after:
      15 of 15 mid-copy and 5 of 5 mid-read, then 18 of 18 on the final code. Full test suite green.
- [x] **Test build 1.2.3.1** with the fix, on GregDeskTop and GregSurface (the Store copy was removed from GregSurface).
      The window title shows only three parts ("v1.2.3"); the log stamps `[v1.2.3.1]`.
- [x] **Test build 1.2.3.2** (2026-10-27), from 5b4c3bc with `<Version>` 1.2.3.2, on GregDeskTop and GregSurface; the
      log stamps `[v1.2.3.2]`. Still to do: the Mac for S11.
- [ ] **A2 (found in M7; already on the 1.2.4 list as "9.3c-2b", now with the second effect added; decide whether it
      stays there): the heartbeat renews whoever's lock is in the file.**
      `WriteLockService.RenewHeartbeatAsync` reads the lock file, sets a new heartbeat and writes it back without
      checking the `SessionGuid`; the ownership check (`heartbeat verify`) runs only after three heartbeat *failures*.
      So after a takeover the old writer goes on acting as writer until its next save, autosave or wake, and until then
      keeps the new holder's lock looking alive (a crash of the new holder is hidden from other computers' stale
      check). D stays safe: every save checks ownership first (R2 holds). Seen in M7: Alice's lock rewritten with our
      heartbeat at 12:29:13 and 12:32:13. Not a regression: the heartbeat has worked this way since multi-user
      (4451a96, March). Likely real-world trigger: the writer's own link down for 4+ minutes while the other computer
      still reaches the share; the takeover happens, the link returns, the next heartbeat succeeds and nothing checks.
- [ ] **A3 (M7; 1.2.4, with item 14): the section editor closes when TermPoint goes read-only**, and its unapplied
      change is dropped without a word: the reload after the demotion runs `SectionListViewModel.LoadCore`, which calls
      `CollapseEditor()` first. Nothing could have been saved anyway; the user just isn't told. (It also means the F4
      hazard can't arise from a section editor.)
- [ ] **A4 (M7; 1.2.4): the refused-write message waits behind any notice already showing** (in M7 a license notice
      left on screen hid it), and each refused Apply queues another copy (R4: one message per problem).
- [ ] **A5 (M7; small, logging only): the choice on "Restore Unsaved Changes?" is not logged.** A kept edit vanished
      between 12:33 and 12:39 and the log could not say whether Restore or Discard had been chosen (most likely
      Discard; the probe at 12:47–12:49 showed Restore working). One INFO line per choice would settle such questions.
- [ ] **A6 (M10; measure, then decide): a slow link stops the open.** With clumsy's Lag 110 ms (in and out, so about
      +220 ms a round trip) every open stopped at `DatabaseValidator`'s `PRAGMA integrity_check` of D on the share
      ("CheckIntegrity timed out after 5000ms", 14:16:24, 14:20:28, 14:21:10; also M2 on 2026-10-22): the check reads
      every page across the share within one 5 s step limit. Saves at the same lag work. Ordinary home VPNs add far less
      than 220 ms; worth finding the latency at which opening starts to fail (and what the user is told) before deciding.
- [ ] **A8 (M5 (3), Must; decide: fix in 1.2.3 or accept): after a takeover, the waking computer can stay
      writer.** GregSurface slept with `ZZZT-302 S` unsaved; GregDeskTop took over and saved; on waking (15:33:53, gap
      483 s) GregSurface's wake check read the lock and found it NOT ours, then re-read 200 ms later "to rule out a
      transient heartbeat-rename overlap", and that re-read hit a sharing violation (almost certainly GregSurface's own
      heartbeat, which fires at the same moment after a wake). `VerifyLockIsOursAtAsync` returns the re-read's result,
      so the verdict became Unreachable and the wake check "kept current state", with no second check. GregSurface
      then acted as writer for 8 minutes, no banner, its heartbeat renewing GregDeskTop's lock (A2), until its next
      Save (15:41:37) found NotOurs twice, refused, demoted with the right banner and kept `ZZZT-302 S`. **D never
      changed** (hash `4E30C00C…` before and after; R1–R3 held); R4 did not. With autosave on (default 10 min) the
      window is at most one autosave interval. **Reproduced in M5 (4)** (16:05:00, after a suspended process rather
      than sleep): the same three log lines, so it looks systematic after a takeover, not a rare race: at wake the
      heartbeat and the wake check run together, and the heartbeat's rename is what the re-read collides with. Also
      seen then: the waking computer's heartbeat rewrites the new holder's lock every minute (A2). **And in M6** (16:41:17)
      the same collision hit the exit save's final check before the rename: the refusal was classed as "location did
      not respond" (a misleading BugSnag report too), so the save treated the lock as still its own and deleted the
      `.tmp` ("D.tmp discarded"). Harmless there, but with per-writer `.tmp` sharing (A11) it could delete the new
      holder's save in progress: a write to the share without the lock (R2). Fix directions: (a) a lock that parses and names another session is
      proof by itself (our own rename can only ever show our own session), so the re-read is needed only for a missing
      or unreadable file; (b) on Unreachable, the wake check tries again shortly instead of giving up; (c) A2's
      heartbeat ownership check would also catch it within a minute.
- [ ] **A11 (M6; on the 1.2.4 list as F12's "one `.tmp` name for every writer"): a writer asleep mid-save blocks
      the new writer's saves.** GregSurface's frozen TermPoint held `BIOL-TT.db.tmp` open, so for 6 minutes every save
      on GregDeskTop failed ("…in use by another program or can't be accessed…"). No data risk (D untouched, the
      changes kept), but the message blames "another program", and a Surface in Modern Standby keeps its network, so
      real sleep can do this too. Per-session temporary names would remove it.
- [ ] **A9 (M5 (3), M10; 1.2.4 with F5): a save with no edits right after "Can't Restore".** `SessionLifecycle`
      keeps `recoverUnsaved` (line 137) set after the open falls back to a normal checkout (Save a Copy… or Discard on
      "Can't Restore"), so the "save restored changes at once" step (line 222) saves a session that restored nothing
      (seen 14:09:46 and 15:45:39). It rewrites D for nothing and, like F5, can turn someone else's later Restore into
      "Can't Restore". Fix: run that save only when the checkout really adopted the kept changes.
- [ ] **A10 (M5 (3); 1.2.4, small): the suggested name for Save a Copy… can already be taken.**
      `RecoveryConflictPrompt` always suggests "<name> (recovered).db", even when that file exists; suggest the next
      free name ("(recovered-2)", …).
- [ ] **A7 (M11; a retention question for Greg): backups turn over fast.** GregDeskTop made a backup at every open
      and keeps five, so five opens in a row deleted every older backup: in M11 the 14:22:08 backup
      was gone by 14:40, and the 14:24:54 backup just restored was gone at the next open. A user troubleshooting a bad
      edit by restarting a few times would lose the backups from before it. Worth a look at count versus age (for
      example, always keep the newest backup from each of the last few days).
- [x] **Moved to 1.2.4 (Greg, 2026-10-27; `spec_version_1.2.4.md`, "Item 5 (M11)"). Probe: a TermPoint process left running with no window** after its main window closed (seen once,
      GregDeskTop, PID 15416, during the restart after a restore at 21:44:48: "Managed teardown complete" but never
      "Dispatcher loop exited", two hidden untitled windows, no lock or guard held). Suspected: the hidden-not-closed
      database chooser or wizard refusing the app's own shutdown. Probe (one computer): open via the chooser, close with
      the X, check Task Manager for a leftover TermPoint.exe.
      **Reproduced 2026-10-27** on GregDeskTop, 1.2.3.2, again on the restart after a Save & Backup restore: PID 20824
      logged "Managed teardown complete" at 14:33:40 and never "Dispatcher loop exited"; it had already released the
      lock and the guard and started the new copy. Its only windows are Avalonia's three hidden helper windows, which a
      healthy TermPoint has too; no main window and **no hidden chooser**, so the chooser theory is ruled out. Idle
      (14 s of CPU, 265 MB), its `running\20824.run` file still present. Plain closes all day (including from the
      chooser, 14:06:39 and 14:43:13) and the chooser restore (no restart) exited normally, so the restart after a
      restore (`SessionLifecycle` → `RelaunchApplication`) is the path to study.
      **Stack and heap (dotnet-stack, dotnet-dump; dump deleted afterwards):** nothing is hung. The main thread is idle
      in Avalonia's message loop (`Dispatcher.MainLoop`), the only other activity is BugSnag's idle delivery queue and
      .NET's own threads. Avalonia's `ClassicDesktopStyleApplicationLifetime`: `ShutdownMode` = OnMainWindowClose,
      `MainWindow` = the TermPoint main window, `_isShuttingDown` = false, and its window list is **empty** (six windows
      opened and six closed; no chooser, dialog or panel object left). So the shutdown that closing the main window
      should trigger either never ran or was abandoned at that moment. Avalonia 12.1.3 abandons it silently when any
      window is still registered after it has closed the windows that have no owner; it skips owned windows (the
      chooser is owned by the main window and refuses every close; the restore's dialogs are owned too). Ordinary
      closes of chooser-opened sessions exit cleanly, so the chooser alone doesn't explain it. **Probe (14:58–14:59):**
      with "Always open the most recent database at startup" on, TermPoint (PID 11056) opened BIOL-TT with no chooser
      window ever created (checked); a Save & Backup restore then left PID 11056 stuck exactly like 20824 ("Managed
      teardown complete" 14:59:36, no "Dispatcher loop exited", no TermPoint windows, idle). **The chooser is not the
      cause; the Save & Backup restore path is.** Its dialogs (confirm, "Restoring the Backup", the result message)
      each finish closing before the code moves on, so the remaining suspects are the relaunch (the new copy is started
      just before the old main window closes) and the stale-session teardown (`EndSessionAfterRestoreAsync`); telling
      them apart needs a test build. A defensive fix works whatever the cause: once the main window has closed, ask
      Avalonia for a forced shutdown. Also worth doing with it: pass the restored database to the relaunched copy, so it
      reopens it instead of showing the chooser. **Impact:** no
      data risk (lock and guard already released, connection disposed, working copy cleaned); an invisible process
      holds memory, can make an MSIX update ask to close the app, and its `running` file reports an unclean end later.
- [ ] **Watch for: the database chooser panel staying on screen** after a database opens (seen once on GregSurface).
      If it does: do not press its buttons; drag its header, then minimise and restore the main window (does it come
      back with it?), and note `winver`.
- [x] **1.2.3's scope for the persistence work, decided 2026-10-26** (Greg, item by item): in 1.2.3 are items 28
      (message pass), 30 (backup cleanup), item 15's exit guard, item 13's SharePoint/Teams detection and a
      Help note for item 26 (later moved to 1.2.4 as a real Save As command); everything else moved to 1.2.4. S5 moved with item 14. Item 29 (F5) was in at first, then
      moved to 1.2.4 the same day after its design and risk review, so the save code the field tests exercise is final
      for 1.2.3. Details: `spec_version_1.2.3.md`, "Work remaining".
- [x] **Decided 2026-10-26:** items 25, 6 and 7 are in 1.2.3; item 27 gets a time-boxed probe, then is fixed in
      1.2.3 or the virtualization switch is hidden in 1.2.3 (then S10, N1 and N2 wait for 1.2.4).
- [x] **Done 2026-10-26 (item 30):** `BackupService` no longer deletes a partial backup with plain, unguarded file
      calls when the backup folder's share is dead; the delete has the copy's network deadline.
      `tools/NetworkProbe/GAP-AUDIT.md` lists 24 more outside the save path (1.2.4).
- [x] **Done 2026-10-26** (9775b1b): the window title shows the fourth version number when it isn't 0
      ("TermPoint v1.2.3.1").
- [ ] **Before release:** `<Version>` 1.2.3.1 was committed (43a0336, 2026-10-25) and is on `Version_1.2.3`. Set it
      back to three numbers in `src/TermPoint/TermPoint.csproj` before the Store and Velopack builds (the Store expects
      the MSIX's fourth number to be 0; Velopack expects three-part versions). Consider 1.2.4 rather than 1.2.3, so
      the test machines on 1.2.3.1 see the release as an update.

## The computers

| Name | Role |
|---|---|
| **Mekler** | The file server. Hosts the share and runs clumsy. **TermPoint never runs here**: if Mekler's name ever appears as a lock holder, stop, something is running where it shouldn't be. |
| **GregSurface** | One user. The one that sleeps (lid shut) in M5 and M6. **Assumed:** swap the two user names in M5–M7 if it's the other way round. |
| **GregDeskTop** | The other user. |

Only M5 and M6 need a particular computer (the one that sleeps). Everywhere else either user computer can be the writer:
swap the names if you prefer (GregDeskTop was the writer in the first M1 runs).

TermPoint shows computer names in capitals (`GREGSURFACE`, `GREGDESKTOP`). Extra computers, only where a scenario asks:
a **third computer** for M5 (6) (a spare PC, a VM or the Mac); a **Windows 10 and a Windows 11** machine for S7–S8 (note
which of yours is which; use a VM for the one you lack); the **Mac** for S11; **1.2.2** for M8 (put it on
GregSurface for that block). A Store 1.2.2 installs beside the sideload 1.2.3 (different package identity), but then both run
on one computer, which switches on TermPoint's same-computer lock rules, so don't use that for M8. Use a sideload 1.2.2 build
instead: it can't be installed over 1.2.3 (lower version), so uninstall 1.2.3 first, after collecting its logs and kept changes.

## Before you start

- **Build.** The sideload MSIX from dev-console task 01 (output: `C:\temp\termpoint-msix\TermPoint.msix` plus
  `TermPoint-cert.cer`). It installs over the previous build with no uninstall, **provided its version is higher**.
  Bump `<Version>` in `src/TermPoint/TermPoint.csproj` for every test build after the first (`1.2.3` → `1.2.3.1` → `1.2.3.2`;
  the script pads to four parts) and put it back to `1.2.3` for the release build. The `[v…]` stamp on each log line shows
  that version, so it is the canary for stale bits. Don't uninstall mid-scenario: it deletes the app's logs and any kept
  unsaved changes. Run black-hole tests detached from the debugger. **The same build on both user computers; never on
  Mekler.** A computer installs it only if it trusts the signing certificate (the script reuses one for 3 years, so this
  is once per computer): elevated PowerShell,
  `Import-Certificate -FilePath <path>\TermPoint-cert.cer -CertStoreLocation Cert:\LocalMachine\Root`
  (or double-click the `.cer` → Install Certificate → Local Machine → Trusted Root Certification Authorities), then
  double-click the `.msix`.
- **Databases: copies only.** Make `FT-master.db` from a real-sized database and put `FT.db` on Mekler's share
  (`\\Mekler\<share>\TermPoint\FT.db`). "D" below means that shared file. Never open it in place with SQLite (DB Browser,
  `sqlite3`): even a read-only open can change its header. The helper scripts are the one exception, and only on the copy.
- **Logs.** `%AppData%\TermPoint\Logs\app-YYYY-MM-DD.log`, one per computer (an MSIX install may write under
  `%LOCALAPPDATA%\Packages\*TermPoint*\LocalCache\Roaming\TermPoint\Logs\` instead; follow whichever file grows with
  `Get-Content <log> -Wait -Tail 5`). Collect them all at the end.
- **Clumsy** (on Mekler, which degrades both users; or on one user's computer to degrade only that one). Filter
  `tcp.DstPort == 445 or tcp.SrcPort == 445`: SMB rides IPv6, so `ip.DstAddr` filters miss it. Type it into clumsy's
  Filtering box instead of picking a preset; it matches IPv4 and IPv6 alike. Tick the row of each effect you want (Lag, Drop)
  with Inbound and Outbound ticked, then press Start. *Degraded* = Lag 80–150 ms + Drop about 2 % (at 4 % some saves give
up on this link, M4 2026-10-27). *Black hole* = Drop 100 %. Ping is not a check: it is ICMP, which the port-445 filter
does not touch; time a file write to the share instead.
  Don't go above about 150 ms of lag: at 300 ms TermPoint's 5 s step limits trip with no black hole at all. Never aim a
  black hole at a particular second of a save: the steps vary too much and clumsy needs ~1 s to switch (see M1).
- **A true crash.** Find the PID in Task Manager → Details, then `taskkill /F /PID <pid>` (or End task on the Details tab).
  Never `/IM`. The Processes-tab "End task" sends a close request, which runs the graceful exit save: that tests exit, not a
  crash. After a hard kill, `FT.lock` must still be on the share.
- **Helper scripts** (repo root, need Python, **copies only**; in cmd, don't start the path with "/"):
  - `python tmp/handtest_lock.py takeover <db>`: "Alice" on ALICE-PC takes the lock now. Run while TermPoint has the copy open for editing.
  - `python tmp/handtest_lock.py stale <db>`: Alice's lock, 10 min old. Run with TermPoint closed, then open the copy.
  - `python tmp/handtest_lock.py clear <db>`: delete the lock file (TermPoint closed).
  - `python tmp/handtest_10a.py colleague <db>`: appends " (colleague)" to the first instructor's last name, written straight into D, as a colleague's save would. Run it again to append again.
  - **`tools\NetworkProbe`** (M1 Part A): checks that every network call TermPoint makes gives up within 5 s when the
    network dies, using TermPoint's real code, and checks its own cut before it gives any verdict. On GregDeskTop, TermPoint
    closed, elevated PowerShell; in that window first `net use \\mekler\Users /user:Mekler\Greg` (an elevated window does not
    share your normal sign-in to Mekler, and the share then reports the folder as missing). Run
    `.\NetworkProbe.exe --share '\\mekler\Users\gregs\temp\handtest'` from `tools\NetworkProbe\bin\Release\net10.0\win-x64`;
    the report lands in the repo's `tmp\netprobe\`. Details: `tools\NetworkProbe\README.md`.
  - `tmp\handtest_savelog.ps1 -Since 12:40` (on the writer): reads the TermPoint log and prints the step that stalled, when the
    save gave up (when the banner is due), any red flags and the recovery save. Useful for M1 Part B.
  - `tmp\handtest_blackhole.ps1` and its launchers: **superseded, don't use for M1.** They aimed a black hole at a moment of the
    save from Mekler; most runs missed, and a miss looks like a pass.
- **Timing constants.** Heartbeat 60 s; reader poll 60 s; stale = heartbeat unchanged for 180 s on the *observing* computer's own
  clock; "Take over" then waits up to ~70 s ("Checking the Lock"; Cancel = Open read-only); per-operation stall deadline 5 s;
  a sleep is noticed after a gap of more than 90 s. Park the lid shut for **4 minutes** whenever a scenario says "sleeps".
- **Reset** (between scenarios unless told otherwise). Close TermPoint everywhere; copy `FT-master.db` over `FT.db`; delete any
  `FT.lock` or `FT.db.tmp` beside it; launch TermPoint once on each user computer. A "Restore Unsaved Changes?" prompt there is
  leftover from the last scenario: Discard it (and confirm).
- **Distinct edits.** Add a section with a new code each time (`ZZZT-101 S`, `ZZZT-102 D`, …). The letter says whose edit it
  is: **S** = GregSurface, **D** = GregDeskTop. The end state then shows exactly which edits survived.
- Don't browse into a dead share with a native Browse… dialog: the OS dialog hangs ~45 s (not a TermPoint bug).

**Standing checks, on every dialog and banner you see.** It says what happened, whether the work is kept, and what to do
next. No "working copy" or "local copy", no paths, no log text. One message per problem, and never a question about
something already dealt with. It says "kept", never "safe". Any breach is a fail: write down the exact text.

**Rules** ([CLAUDE.md](../../CLAUDE.md)): **R1** D only changes by one atomic rename of a copy that was checked. **R2** Never write D
unless the lock is ours and D is exactly as we last saw it. **R3** Never delete the only copy of unsaved work until
another copy exists. **R4** Never confuse the user.

---

## Tier 1 — MUST

*Why: a wrong answer here loses or overwrites someone's work (save, lock, takeover, exit, restore, crash recovery, mixed
versions, F1, F4, F8, F12). Nothing ships until every Must passes.*

### M1 — Network cut mid-save (July A1–A3)
*Part A: GregDeskTop alone, ~35 min · Part B: GregSurface, Mekler, ~15 min · R1 R2 R3 R4*

**Why it is in two parts (2026-10-22).** The first M1 runs aimed clumsy black holes from Mekler at chosen seconds of a save.
Most missed (a save's steps took anywhere from 0.4 to 8.5 s, clumsy needs about 1 s to switch, and a miss looks exactly
like a pass), but two runs (14:03, 14:48) exposed a real defect, **A1**: a cut during the copy could go unnoticed for the
whole outage, with no log line, no banner and "Saving…" until the network came back. It is fixed ("Open issues" above). So
the steps are now tested by a rig that proves its own cut (Part A), and real TermPoint is only asked what the user sees
(Part B).

**Part A — every network call gives up in time (`tools\NetworkProbe`, see "Helper scripts").**
*Setup:* GregDeskTop, TermPoint closed, no clumsy running on any computer; elevated PowerShell with the `net use` done.
The probe must contain the code under test: rebuild it after any change to `NetworkFileOps`.
1. `.\NetworkProbe.exe --share '\\mekler\Users\gregs\temp\handtest'` (all 18 cases, 30–40 min; it cuts GregDeskTop's file
   sharing on and off, so use no other network drives meanwhile).

**Expect:** both controls OK; every case PASS (about 5 s after the cut for the cut-before cases, about 2 s for the
mid-copy and mid-read cases); `hash-cm` caught in `ComputeHash (read)`; no call-return over 1 s. NOT TESTED is not a
failure: re-run that case with `--only <case>`.
**FAIL:** any FAIL, above all "completed only AFTER the network came back" (the A1 signature) or "blocks its caller". If
a control fails, the rig is not working and the run says nothing about TermPoint.
**Done so far (2026-10-22, reports in `tmp\netprobe\`):** full run before the fixes: 17 pass, 1 fail (copy-cm, A1); after
the write fix: copy-cm 10/10; after the read fix: hash-cm 5/5 caught in the read, copy-cm 5/5.
**Final run (2026-10-22 19:35, the committed fix b193eb8):** 18 of 18 passed, both controls OK; report `tmp\netprobe\netprobe-20261022-193513.md`.
**Result:** ☒ pass ☐ fail / notes: 18/18 on the final code (copy-cm caught in the write, hash-cm in the read, every cut-before case at 5.0 s; no call held up its caller).

**Part B — what the user sees, in real TermPoint.** Not aimed at any step: Part A covers the steps.
*Setup:* GregSurface is writer on `FT.db` (1.2.3.1 or later), autosave on. Clumsy on Mekler: **Lag 110 ms only**, so a
save shows "Saving…" for about 15 s.
1. Add `ZZZT-101 S`, click **Save**, and while "Saving…" shows, tick Drop at 100 (any moment).
2. Watch for about a minute. Untick Drop, Save again. Close TermPoint and reopen.
3. Twice more (`ZZZT-102 S`, `ZZZT-103 S`), cutting at a different moment each time.

**Expect:** a "can't reach the database" banner within about 15 s of the cut; the window stays responsive (no "Not
responding"); no exception dialog; still writer; once the network is back the next save lands and the edit is there
after reopening; D opens cleanly. A save that finishes before the cut takes effect is just a normal save: cut earlier.
**Log** (`tmp\handtest_savelog.ps1 -Since <time>` prints it): a `CopyFile (…)`, `ComputeHash (…)` or `… timed out`
line, then `File.Delete timed out` for the leftover `.db.tmp`, and later `abandoned … task faulted after timeout`.
**FAIL:** "Saving…" stuck with no banner and nothing logged until the network returns (A1 again), any freeze, an
exception dialog, a demotion to read-only, or a save that doesn't land after recovery.
**Result:** ☒ pass ☐ fail / notes: 2026-10-22, GregDeskTop, 1.2.3.1 (user): three cuts at about 3, 8 and 13 s into the save, all as expected.
Log check (Claude): stalls caught in `CopyFile (write)` at 20:20:27 and 20:23:32 (where A1 used to hide) and in
`ComputeHash (read)` at 20:20:57 and 20:22:21, each save gave up about 10 s after the cut; the heartbeat check kept the
session writer ("lock still ours"); saves landed afterwards (20:21:51, 20:23:07, 20:24:02). No A1 signature.

*Earlier note, from the first hand-timed runs (2026-10-22, before the log analysis):* "A1: pass, A2 pass A3 pass".
The analysis then showed most A1 and A3 runs had missed, and two A1-type runs had shown the A1 defect; the A2 runs
(hash check, 13:58 and 14:53) were genuine passes. Superseded by Parts A and B.




### M2 — Kill the writer (July C1, C2, C3, B4)
*GregSurface, Mekler · R1 R3 · ~30 min*
**Setup:** GregSurface writer. Clumsy on Mekler: **Lag 110 ms only** (no Drop), so a save shows "Saving…" for about 15 s.
Hard-kill only (see preface). Kill by watching "Saving…", not by the second: save a couple of times first to see how long it
shows. Have the `taskkill /F /PID <pid>` command typed and ready.
- **C1 (early).** Add `ZZZT-201 S`, Save, kill soon after "Saving…" appears. Note any `FT.db.tmp` on the share. Clumsy off, relaunch TermPoint.
  **Expect (also B4):** the lock is reclaimed at once, no 3-minute wait and no lock prompt; "Restore Unsaved Changes?" appears
  (D didn't change). **Do M3 here**, then Restore: `ZZZT-201 S` is present, Save works, a leftover `.tmp` is swept (or at least harmless).
- **C2 (late).** Same, kill as late as you can while "Saving…" still shows. **Expect either:** (a) D already holds the edit and TermPoint recognises it, with no conflict banner
  (log `source matches pending save hash — our delayed rename landed; treating prior save as successful.`); or (b) D unchanged,
  Restore offered and restores the edit. D opens cleanly either way.
- **C3 (control).** Open, save, sit idle, kill. **Expect:** relaunch has no prompt and goes straight to work.

**Log:** `Reclaimed lock from dead session (PID …, user@machine).`
**FAIL:** an edit silently lost with no offer; a corrupt D; a false "changed elsewhere" banner against our own half-landed save;
"Can't Restore Unsaved Changes" when D already holds the edit (that is F8 coming back); a lock that isn't reclaimed.
**F8 can't be produced by hand:** the crash must fall in the milliseconds between the rename and the marker delete. The harness's
crash sweep covers it (`Save.AfterRename`); C2 is the nearest hand test.
**Result:** ☒ pass ☐ fail / notes (C1, C2, C3): 2026-10-22, GregDeskTop, 1.2.3.1, `BIOL-TT.db` on Mekler. C1 and C2
pass. C3 re-run with care (Save, 30 s untouched, switch to Task Manager, kill): "Unsaved changes" never came back and the
relaunch asked nothing, so C3 passes; the first C3's prompt most likely followed a change after its last save (see
below). Log check (Claude): four kills, each relaunch reclaimed the lock at once ("Reclaimed lock
from dead session"). A mid-save kill with an unsaved edit was offered Restore; its leftovers were swept (local `.savetmp`
20:29:58, `BIOL-TT.db.tmp` on the share 20:30:58) and the next save landed. The kill around 20:32 came just after the save
at 20:31:59 had landed: no prompt, working copy cleared (C2 outcome (a)). **C3 (kill after the 20:32:50 save, while
idle): the user saw "Restore Unsaved Changes?"**, and the 20:33:39 relaunch shows the unsaved-changes marker was present.
Expected: no prompt. Open question: did anything change after 20:32:50 (an edit, or a write on focus loss when switching
to Task Manager)? Two launches while the link was still impaired (20:25, 20:33) stopped at the integrity check
("CheckIntegrity timed out") and closed.

### M3 — F1: the X and Discard on "Restore Unsaved Changes?"
*GregSurface (at M2's C1 relaunch) · R3 R4 · ~5 min*
1. At "Restore Unsaved Changes?" click the window's **X**.
2. Click **Discard**. "Discard Unsaved Changes?" appears ("Your unsaved changes from the previous session will be deleted. This can't be undone.") with **Discard** and **Go Back**.
3. Click that dialog's **X**, then Discard again and **Go Back**.
4. Finish with **Restore** and carry on with C1.
5. Optional, last: kill again with an unsaved edit and this time confirm **Discard**; the edit is gone and the next launch asks nothing.

**Expect:** steps 1 and 3 and Go Back each return to "Restore Unsaved Changes?" and nothing is deleted (the edit still restores at step 4);
only the second dialog's **Discard** button deletes. The same holds on "Can't Restore Unsaved Changes" (seen in M5 (3)); its
confirmation adds "To keep them, go back and choose Save a Copy…".
**Result:** ☒ pass ☐ fail / notes: 2026-10-22, GregDeskTop, 1.2.3.1, local test copy `tmp\f4-test`: all steps as expected (user).

### M4 — Two-user contention (July B1, B2, B3, B5)
*GregSurface, GregDeskTop, Mekler · R2 R3 · ~35 min*
**Setup:** GregSurface opens first (writer), GregDeskTop second.
- **B1.** GregDeskTop opens read-only ("Read-only — held by … on … since …"). Clumsy degraded; leave both ≥ 6 min while GregSurface saves every couple of minutes.
  **Expect:** GregDeskTop is never offered write access; GregSurface stays writer and its saves land (slowly). Log on GregDeskTop may show `Poll: lock read contended (…) — skipping cycle`.
  **FAIL:** any takeover offer while GregSurface is alive. **B5 (bonus):** repeat while GregSurface is inside a long degraded save.
- **B2.** Clumsy off; close GregSurface normally. **Expect:** within ~60 s GregDeskTop shows **Take over as writer** (log `Poll: lock file gone — write access available.`);
  accept, edit, save works; relaunch GregSurface: read-only, naming GregDeskTop.
- **B3.** GregSurface writer, GregDeskTop reader. GregSurface makes an unsaved edit and is hard-killed; confirm `FT.lock` is still there. **Expect:** GregDeskTop stays read-only
  ~3 min, then (3–4 min) is offered takeover (log `Poll: lock heartbeat unchanged for …s — write access available.`); accept, edit, save.
  Relaunch GregSurface: "Restore Unsaved Changes?" → Restore → "Unsaved Changes Kept" (someone else is editing; offered again at its next write-access open).

**Result:** ☒ pass ☐ fail / notes (B1, B2, B3): 2026-10-27, 1.2.3.2 on both, `BIOL-TT.db` on Mekler (user; hashes,
lock file, GregDeskTop's log and copies of D and the kept changes checked by Claude). **B1:** GregSurface writer,
GregDeskTop reader, clumsy on Mekler from about 13:22 to 13:32. At Lag 110 + Drop 4 % a 1 MB write to the share took
25–28 s and some of GregSurface's saves gave up ("TermPoint can't reach the database location right now…", one notice,
cleared by the next good save); at Drop ~2 % the saves landed (last 13:31:49). The reader skipped two poll cycles on
timeouts (13:22:46 `File.Exists`, 13:23:46 `ReadAllText`) and was never offered write access; GregSurface's lock kept
the same session and a fresh heartbeat throughout. Refresh on GregDeskTop (13:36:31) showed every saved edit. B5 not
run separately. **B2:** GregSurface closed normally; 13:38:41 `Poll: lock file gone — write access available.`;
GregDeskTop took over at 13:39:08 and its saves landed (13:39:26, 13:39:31); GregSurface reopened read-only naming
GREGDESKTOP, with the edit. **B3** (roles swapped: GregDeskTop writer; a first run was redone because both computers had
edited the same section): GregDeskTop applied `ZZZT-901 D` (marker 13:54:54, edit confirmed in the working copy) and
was killed about 13:55; its lock stayed; GregSurface was offered the takeover, took the lock at 13:59:34 and saved
`ZZT-902 S` (14:00:08). GregDeskTop's relaunch (14:00:17) reported the unclean end (item 7), offered Restore, then said
someone else was editing, and opened read-only naming GREGSURFACE. D holds `ZZT-902 S` and not `ZZZT-901 D`; the kept
copy holds `ZZZT-901 D` with its marker unchanged. **Clumsy setting:** the doc's "degraded" Drop of 3–5 % is too harsh
for this link (saves give up at 4 %); use Lag 110 + Drop about 2 %.

### M5 — Stale-lock takeover (spec item 9)
*GregSurface (sleeps), GregDeskTop, Mekler; a third computer for (6) · R2 R3 · ~90 min*
**Setup:** GregSurface writer; GregDeskTop closed. For each "sleeps": add an unsaved edit on GregSurface, shut the lid 4 min.
Dry run of the prompt on one computer: `python tmp/handtest_lock.py stale <copy>` with TermPoint closed, then open the copy.
1. **(1) Prompt.** GregSurface edits `ZZZT-301 S` unsaved, sleeps. GregDeskTop opens `FT.db`: **"Database Lock Is Inactive"** names GregSurface's user and computer and the idle minutes
   ("…hasn't been active in this database for N minutes. Their computer may be asleep or disconnected. If you take over, changes they haven't saved yet can't go into the database — they'll only be able to save them as a separate copy."), buttons Take over / Open read-only. Leave it open.
2. **(2) Holder wakes.** Open GregSurface's lid; wait for its heartbeat (log `Wake detected — gap was …s.`, `wake check — lock confirmed. Renewing heartbeat.`). GregDeskTop clicks **Take over**
   (maybe "Checking the Lock" first). **Expect** "Database Is In Use Again" ("<user> is active in this database again, so it will open read-only."); GregDeskTop is read-only; GregSurface's lock is intact and it keeps editing and saves.
   Log: `ForceAcquire: lock changed since it was judged stale — leaving it alone (now …)`.
3. **(3) Taken over for real.** GregSurface edits `ZZZT-302 S` unsaved, sleeps. GregDeskTop opens, takes over (wait ≤ ~70 s), adds `ZZZT-303 D`, saves. Open GregSurface's lid.
   **Expect** the banner "Write access was taken over by <user> on GREGDESKTOP. Your unsaved changes have been kept — TermPoint will offer to restore them the next time you open this database with write access. You can regain write access by exiting and restarting TermPoint."; GregSurface is read-only.
   Log (GregSurface): `wake check — lock is no longer ours. Timing out session.`, `DemoteToReadOnly — preserving D' and dirty marker (unsaved changes kept for recovery).`
   Close TermPoint on GregDeskTop. Restart it on GregSurface: "Restore Unsaved Changes?" → Restore → "Can't Restore Unsaved Changes" → **Save a Copy…** (suggests `FT (recovered).db`). The copy has `ZZZT-302 S`; D has `ZZZT-303 D` and not `ZZZT-302 S`.
   **F1 angle:** on "Can't Restore", X re-asks; Discard asks "Discard Unsaved Changes?" before deleting.
4. **(4) Nobody changed D.** As (3), but GregDeskTop takes over and closes without edits. GregSurface restarts: Restore. **Expect:** its change restores into D. **Known (review F5, fix moved to 1.2.4):** a session that closes without edits can still rewrite D, which turns this into "Can't Restore" → Save a Copy…. It happens only when D was last written by something other than a TermPoint save (a new database, a restored backup, a 1.2.2 direct open, or the first session after the SQLite update in 1.2.3); otherwise expect the restore to work. Either way the work is offered back: record which, and what last wrote D.
5. **(5) Reader waits.** GregSurface asleep 4 min, GregDeskTop opens → **Open read-only**. Open GregSurface's lid, let it resume, then exit it cleanly. **Expect:** GregDeskTop's **Take over as writer** appears within ~60 s and works.
6. **(6) No double takeover (needs a third computer; skip and say so if you have none).** GregSurface asleep 4 min. GregDeskTop opens (prompt, leave it open), then the third computer opens (prompt too). GregDeskTop takes over; then the third computer clicks Take over. **Expect:** it gets "Database Is In Use Again" naming GregDeskTop and is read-only; never two writers (check the lock file's holder and each banner).
7. **(7) Clock skew.** Turn off auto time sync on GregSurface and set its clock 10 min **behind**; it stays active as writer. GregDeskTop opens. **Expect:** a prompt may show (the clock is only a hint), but Take over ends in "Database Is In Use Again" and GregSurface never loses the lock. Then its clock 10 min **ahead**, hard-killed: GregDeskTop's polling finds the dead lock after ~180 s. **Restore the clock afterwards.**

**Result:** ☐ pass ☐ fail / notes ((1)…(7); which computer was the third): 2026-10-27, 1.2.3.2 on both,
`BIOL-TT.db` on Mekler reset from the master; GregSurface (a Surface: no lid, so Start → Sleep; its heartbeat was
watched on the share and stopped each time, e.g. last beat 15:14:58, then nothing for 5 minutes) (user; lock file,
hashes, both logs and copies checked by Claude). **(1) ☒ pass:** "gregs on GREGSURFACE hasn't been active in this
database for 6 minutes. Their computer may be asleep or disconnected. If you take over, changes they haven't saved yet
can't go into the database - they'll only be able to save them as a separate copy", Take over / Open read-only;
GregDeskTop log 15:21:07 `Stale lock detected for gregs@GREGSURFACE`. **(2) ☒ pass:** GregSurface woke (`Wake detected
— gap was 472s`, `wake check — lock confirmed`, heartbeat 15:23:20); GregDeskTop's Take over then got "gregs is active
again…" and opened read-only, log 15:23:55 `ForceAcquire: lock changed since it was judged stale — leaving it alone`;
GregSurface kept the lock and its save landed (15:25:02). **(3) ☒ fail on waking, the rest pass:** GregDeskTop took
over (15:32:20, after the confirmation wait) and saved `ZZZT-303 D`; GregSurface woke at 15:33:53 but stayed writer
with no banner for 8 minutes (A8) until a Save at 15:41:37 refused, demoted with the right banner ("gregs on GREGDESKTOP
has taken over editing…") and kept `ZZZT-302 S`; D's hash unchanged throughout. Then GregDeskTop closed; GregSurface's
restart offered Restore → "Can't Restore Unsaved Changes" (X re-asked; Discard asked first; Go Back) → Save a Copy… →
`BIOL-TT (recovered-3).db` (the suggested name was taken: A10). The copy holds `ZZZT-301 S` and `ZZZT-302 S`; D holds
`ZZZT-301 S` and `ZZZT-303 D`, not `ZZZT-302 S`; both pass the integrity check. D was rewritten at 15:45:39 by a save
with no edits right after the reopen (A9; the bytes came out identical, hash `4E30C00C…` unchanged).
**(4) ☒ pass (the restore went in), A8 seen again:** GregSurface's sleep was cut short by a Modern Standby self-wake
(15:55:12, its wake check found the lock still its own and renewed it, so no takeover was possible); the test then
froze TermPoint instead (Resource Monitor → Suspend Process; last heartbeat 15:56:35). GregDeskTop took over at
16:04:10 and made no edits. On resume GregSurface hit A8 exactly as in (3) (16:05:00: `NotOurs`, the re-read
contended, `network unreachable, keeping current state`) and stayed writer; its heartbeat then rewrote GregDeskTop's
lock every minute (A2: heartbeats at 16:09:00 on GregSurface's rhythm). A Save on GregSurface refused and kept
`ZZZT-304 S`; GregDeskTop closed with no edits (its exit save left D's bytes unchanged); GregSurface's restart offered
Restore and `ZZZT-304 S` went straight into D (saved 16:12:09; D holds `ZZZT-301 S`, `ZZZT-303 D`, `ZZZT-304 S`;
integrity ok). **(5) ☒ pass:** TermPoint on GregSurface frozen (last heartbeat 16:14:09); GregDeskTop opened at
16:20:13 (stale-lock prompt) → Open read-only (16:20:15); GregSurface resumed and exited cleanly; 16:22:15 `Poll: lock
file gone — write access available`; Take over as writer worked (lock acquired 16:22:26). No offer appeared before
GregSurface came back (the reader's own 180 s had not run out). **(6)** not run (no third computer).

### M6 — F12: the laptop sleeps mid-save
*GregSurface (sleeps), GregDeskTop, Mekler · R1 R2 R3 · ~30 min, a race: allow a retry*
**Setup:** GregSurface writer with `ZZZT-401 S` unsaved. Clumsy **on GregSurface** (not Mekler, so GregDeskTop runs at full speed), degraded so the exit save takes ~15–20 s (time a dry run first).
1. GregSurface: click **X** (exit save starts, footer "Saving…"). ~10 s in, after the D.tmp copy but before the end, shut the lid.
2. After 4 min GregDeskTop opens → "Database Lock Is Inactive" → **Take over**, adds `ZZZT-402 D`, **Save**, and confirms it landed (log `Save completed successfully.`).
3. Open GregSurface's lid. Let it settle for 2 min.
4. GregDeskTop reopens `FT.db` (or Refresh): `ZZZT-402 D` is there; D opens cleanly. Close TermPoint on GregDeskTop.
5. Relaunch on GregSurface: "Restore Unsaved Changes?" → Restore → "Can't Restore Unsaved Changes" → Save a Copy…; the copy has `ZZZT-401 S`.

**Expect:** GregDeskTop's save survives and GregSurface's rename never lands over it (R1, R2). GregSurface tells the user its changes weren't saved but were kept ("Changes Not Saved … They have been kept, and TermPoint will offer to restore them the next time you open this database.", or the lock-lost banner). Its work is offered back (R3).
**Log (GregSurface):** `the final check before the rename refused the save — lock lost, D.tmp left for the new holder's cleanup.` (or the `wake check — lock is no longer ours` lines).
**FAIL:** `ZZZT-402 D` is missing from D, or `ZZZT-401 S` isn't offered back. If GregSurface's SMB session simply died on wake and the save failed early, the end state still passes but the F12 window wasn't hit: retry with the lid closed a little later.
**Result:** ☒ pass ☐ fail / notes (was the window hit?): 2026-10-27, 1.2.3.2 on both, `BIOL-TT.db` on Mekler (user;
share files, both logs and copies checked by Claude). **Window hit:** instead of sleep (a Surface self-wakes, see M5
(4)), TermPoint on GregSurface was frozen (Resource Monitor → Suspend Process) about 7 s into a 15 s exit save under
clumsy Lag 110 ms on GregSurface: `BIOL-TT.db.tmp` was complete on the share (16:30:41), D not yet replaced. GregDeskTop
took over at 16:36:21 (`ForceAcquire`), but could not delete the leftover `.tmp` nor save: six attempts 16:36:50–16:38:52
failed `D.tmp copy failed — file in use` because the frozen process still held `.tmp` open ("TermPoint couldn't save
because the database file is in use by another program or can't be accessed…"; A11). Unfrozen at 16:41:16 (gap 652 s),
GregSurface's **final check before the rename refused the save** and D was not replaced; it showed "Changes Not Saved —
Your latest changes couldn't be saved to the database. They're kept on this computer…" and closed (16:42:28). Its
re-read hit A8's collision, so the refusal was classed as "location did not respond" and it deleted its own `.tmp`
("D.tmp discarded") instead of leaving it for the new holder: harmless here, but see A8. GregDeskTop's next save landed
(16:42:39): D holds `ZZZT-402 D`, not `ZZZT-401 S`. GregDeskTop closed; GregSurface's restart → Restore → "Can't
Restore" → Save a Copy… `BIOL-TT (recovered-4).db`, which holds `ZZZT-401 S`. Both pass the integrity check.

### M7 — F4: an editor open across a lost lock
*GregSurface; any computer with Python for the script · R3 R4 · ~10 min*
**Setup:** writer on a copy of `FT.db` (any computer; a local copy is enough). Dismiss any notice already showing (a
license reminder, say): the refusal message queues behind it (A4). In Save & Backup type **1** in the auto-save interval
and keep the toolbar's Auto-save ticked.
1. Open a **course** in the Courses flyout and change a field. Do **not** Apply, and don't press Ctrl+S: inside an
   editor Ctrl+S is that editor's Apply. (A section editor won't do: the reload after the demotion closes it, A3.)
2. Run `python tmp/handtest_lock.py takeover <path to the copy>` (Alice takes the lock; if it reports a sharing error, run it again).
3. Wait up to a minute for autosave to find Alice's lock. The heartbeat alone never notices (A2); clicking Save also
   works, but only with the flyout closed. Expect the banner "Alice on ALICE-PC has taken over editing this database,
   so it's read-only for you now. …" and the standing read-only row "Read-only — held by Alice on ALICE-PC since …"
   (that row is every read-only session's status, not a second message). The course editor is still open.
4. In the still-open editor click **Apply**.
5. Cancel the editor. Optionally try another write (an Instructors note, focus-loss save).
6. Close TermPoint; `python tmp/handtest_lock.py clear <copy>`; reopen: the change isn't in the database. Set the
   auto-save interval back to 10.

**Expect:** exactly **one** message per Apply, "You no longer have write access, so this change wasn't saved."; no red
or log-text banner, nothing written.
**Log:** `lock verification returned NotOurs`, `mid-session write-lock loss (reason=TakenOver) — demoting to read-only
in place.`, then one WARN line `You no longer have write access, so this change wasn't saved.` per Apply.
**Result:** ☒ pass ☐ fail / notes: 2026-10-27, GregDeskTop, 1.2.3.2, local copy `tmp\m7-test` (user; log and file
checks by Claude). **Nothing was written after the lock was lost:** the course editor's Apply was refused three times
(12:39:32, 12:39:46, 12:41:05, one WARN line each with the plain sentence) and the database stayed byte-identical to
the master throughout; each demotion that had unsaved changes kept them (12:33:10, 12:48:18). Not as first written:
in runs 1–2 the section editor was closed by the reload after the demotion (A3), so step 4 moved to a course editor;
the heartbeat never noticed the takeover (A2), so detection came from Save (12:15:40, 12:18:50, 12:33:09) or autosave
(12:39:09); the plain message was hidden behind a license notice left showing (A4). **Restore probe** (12:47–12:49):
Apply wrote the marker; the lost lock kept the working copy and marker; both survived the close; Restore brought the
edit back and saved it at once (12:49:32), the save then removing the marker. A kept edit that vanished between 12:33
and 12:39 was most likely an unlogged Discard (A5). Incidental: the stale-lock prompt and its confirmation wait worked
(Alice's 5-minute-old lock, 12:24:03 → taken over 12:25:13); BugSnag got "Write lock lost: taken over by another
session" with `newHolder=other` (N3, item 7).

### M8 — Mixed 1.2.2 and 1.2.3 on one share
*GregDeskTop (1.2.3), GregSurface (temporarily 1.2.2), Mekler · R2 R4 · ~30 min, plus reinstalling 1.2.3*
**Setup:** on a copy of `FT.db`, plant an unknown field: `UPDATE Instructors SET data = json_set(data,'$.zzFutureField','keep-me') WHERE id = (SELECT id FROM Instructors ORDER BY last_name, first_name LIMIT 1)`. Collect GregSurface's 1.2.3 logs, then swap it to 1.2.2.
- **Dir 1, 1.2.3 first.** GregDeskTop opens (writer), edits that first-alphabetical instructor (any field), saves, closes. **Expect:** `json_extract(data,'$.zzFutureField')` on a *copy of D* is still `keep-me`. With GregDeskTop writing, GregSurface (1.2.2) opens read-only naming it (it uses Files → Open Database, not the wizard). After GregDeskTop closes, GregSurface edits and saves; GregDeskTop reopens D with no error.
- **Dir 2, 1.2.2 first.** GregSurface writer (again via Files → Open Database); GregDeskTop opens read-only naming it; GregSurface closes; GregDeskTop is offered **Take over as writer** within ~60 s; edit and save work.
- **Dir 3, the known trigger.** GregDeskTop editing; GregSurface (1.2.2) joins through the wizard (1.2.2 opens D directly and rewrites its header). **Expect:** GregDeskTop's next save is refused with "TermPoint can't save your changes: the database was changed elsewhere since you opened it…" and its version is kept via **Save a Copy…**. This is the release-note case: update every computer in a department together.
- **Gate.** 1.2.3 stamps nothing (`ThisReleaseRequires` is null): after a GregDeskTop save, `AppConfiguration` has no `MinimumVersionToWrite` row (log `version gate — stamp none, running 1.2.3.`). To exercise the gate, on a copy `INSERT OR REPLACE INTO AppConfiguration(key,value) VALUES('MinimumVersionToWrite','9.9.9')`: GregDeskTop opens read-only ("Read-only — this database needs TermPoint 9.9.9 or later to edit. …"); 1.2.2 on GregSurface ignores it and edits (as expected for 1.2.2).
- Info only: 1.2.2 editing the planted instructor drops `zzFutureField` (1.2.2 has no catch-all).
- **Afterwards** put 1.2.3 back on GregSurface (bump `<Version>` if the install refuses).

**Result:** ☐ pass ☐ fail / notes (Dir 1, 2, 3, gate):

### M9 — A colleague joins while an editor session is active (item 4)
*GregSurface, GregDeskTop (wizard), Mekler · R2 · ~10 min*
1. GregSurface writer, with an edit pending. Note D's hash (`Get-FileHash \\Mekler\<share>\TermPoint\FT.db`).
2. GregDeskTop: first run (reset its settings) → wizard → existing database → join `FT.db`. It opens read-only naming GregSurface; browse around, no edits.
3. Hash D again, then GregSurface: Save.

**Expect:** GregDeskTop never touches D (hash unchanged); GregSurface's save succeeds with no conflict banner.
**Result:** ☒ pass ☐ fail / notes: 2026-10-27, 1.2.3.2 on both, `BIOL-TT.db` on Mekler reset from the master (user; hashes
and GregDeskTop's log by Claude). GregDeskTop reached the wizard from the startup chooser (Create new → Start Setup
Wizard → existing database), no settings reset needed. D's hash `925AD29A…` (= the master) at 13:00:36 with
GregSurface writing and `ZZZT-801 S` applied, and the same at 13:03:19 after GregDeskTop joined; GregSurface's lock
untouched and still beating. GregDeskTop's log: `Read-only mode; lock held by gregs@GREGSURFACE`, `read-only setup —
D'' created`. GregSurface's Save landed at 13:03:44 (new hash, no `.tmp` left), no banner.

### M10 — Exit save on a slow or dead link
*GregSurface, Mekler · R3 · ~15 min*
- **(a) Degraded.** Add `ZZZT-501 S`, click **X** once. **Expect:** "Saving…" in the footer, the window closes after the save (≤ ~1 min), the lock is gone (log `Released write lock: …`, `[Shutdown] Managed teardown complete; closing main window.`); reopen: the edit is present, no prompt.
- **(b) Black hole.** Add `ZZZT-502 S`, set Drop to 100 %, click **X** once. **Expect:** after the stall, "Changes Not Saved": "Your latest changes couldn't be saved to the database. They're kept on this computer. TermPoint will offer to add them to the database the next time you open it for editing. If someone else changes the database first, they can't be added, but you'll be able to save them as a separate copy." (item 28's wording). Network back, relaunch: "Restore Unsaved Changes?" → Restore → the edit is present and saves.
- Open TermPoint **before** starting clumsy: with Lag 110 ms the open stops at the integrity check of D (A6).
- Repeated X and edits during the exit save are S4.

**Result:** ☒ pass ☐ fail / notes (a, b): 2026-10-22, GregDeskTop, 1.2.3.1, both parts as expected (user). Log check
(Claude): (a) exit save landed 20:42:05, lock released 20:42:06. (b) exit save refused at its first check 20:44:13
("lock verification timed out"), lock deliberately left in place ("Suspended"), changes kept; two launches while the link
was still dead stopped at the integrity check without touching the kept changes; at 20:44:51 the relaunch reclaimed its own
lock at once, Restore was offered, and the restored edit saved at 20:45:00.
**Re-run on 1.2.3.2** (☒ pass), 2026-10-27, GregDeskTop, `BIOL-TT.db` on Mekler reset from the master (user; log and
copies of D checked by Claude). (a) Lag 110 ms only, started after the open: `ZZZT-921 D`, X; "Saving and closing…"
covered the window; three more X clicks ignored (14:23:08–09, one log line each, S4); exit save landed 14:23:20, lock
released 14:23:21, no TermPoint process left; reopened with no prompt. (b) `ZZZT 922 D2`, Drop 100 %, X: 14:28:39 `lock
verification timed out` → save refused, lock deliberately left (`Suspended`), working copy and marker kept; the user saw
the "Changes Not Saved" text above. Network back: 14:30:39 the relaunch reclaimed its own lock at once, Restore was
offered, and the restored edit saved at 14:30:40. D then held `ZZZT-921 D`, `ZZZT-922 D` (an earlier try whose exit
save landed before the cut) and `ZZZT 922 D2`. An earlier (a) try at Lag 110 + Drop 2 % failed its exit save at the
D.tmp check (14:12:41) and behaved like (b); its edit reached D after Restore. Two opens with clumsy already on stopped
at the integrity check (14:20:28, 14:21:10: A6).

### M11 — Restore from backup (item 5)
*GregSurface, GregDeskTop, Mekler · R1 R2 R3 · ~30 min*
**Setup:** GregSurface writer with a backup folder set and at least one backup listed under Save & Backup. Do (a), (c), (d) before (b): (b) replaces D.
- **(a) Black hole mid-copy.** Save & Backup → pick a backup → confirm ("This replaces the database for everyone who uses it. TermPoint first saves your current changes…"). Degrade, then Drop 100 % while it copies. **Expect:** a message that the database is unchanged; D's hash is as before; the session continues and still saves. Repeat from the startup chooser (Preferences → turn off "Always open the most recent database at startup").
- **(c) Reader can't restore.** GregDeskTop (read-only): restore from Save & Backup is refused.
- **(d) Chooser vs. an active editor.** GregSurface is editing `FT.db`; on GregDeskTop restore from the chooser onto `FT.db`. **Expect:** refused, naming GregSurface: "<user> on GREGSURFACE is editing this database, so it can't be restored right now. Restore it after they've closed it…".
- **(b) Success.** Network back; GregSurface restores. **Expect:** "The backup was restored. The database as it was just before the restore is kept beside it as …"; TermPoint restarts; `FT (before restore yyyy-MM-dd HH-mm).db` sits beside D and opens; D holds the backup's content. Chooser variant: the same copy appears and the database opens.

**Result:** ☐ pass ☐ fail / notes (a, b, c, d): (a) ☒ pass, 2026-10-22, GregDeskTop, 1.2.3.1 (user, log check by
Claude). Save & Backup restore on `BIOL-TT.db` with the link cut: 21:38:41 "hashing the database — location
unreachable (database unchanged)"; 21:41:19 and 21:42:31 "restore not started — the save before it ended with
CopyError"; the session carried on each time. Chooser restore on the local test copy `tmp\item5d-test`: with the network
up it restored and restarted (21:44:42); with the backup location black-holed it stopped at 21:45:32 "staging the backup —
location unreachable (database unchanged)" and the chooser showed "Cannot reach the database…". The ~45 s wait before
that notice is Windows' file dialog on a dead share (as in S6). Also seen: the session left open with the S6(b) banner
stayed up through the whole S6(b)/(c) black hole (20:56 to 21:27, about 30 minutes, deliberate), retrying its heartbeat
every minute and keeping its writer state ("share unreachable; keeping writer state"); once the network was back the
banner's Discard Changes worked and the database reopened (21:27:48). (c) ☒ pass and (d) ☒ pass, 2026-10-27, 1.2.3.2,
`BIOL-TT.db` on Mekler, GregSurface writing (user; hashes and GregDeskTop's log by Claude): (c) GregDeskTop, read-only,
Save & Backup offered no restore ("Only the person editing this database can restore it."); (d) GregDeskTop's startup
chooser refused the restore naming GregSurface, log 14:06:07 `RestoreIntoAsync — the database is locked by someone
else; nothing was touched.` D's hash `C277C0EF…` was the same before (14:02:22) and after (14:06:40); GregSurface's lock
untouched. Closing GregDeskTop from the chooser afterwards (14:06:39) left no TermPoint process running (one data
point for the "process left running with no window" probe in Open issues). (b) ☒ pass, 2026-10-27, GregDeskTop
writer, 1.2.3.2 (user; copies of D, the backups and the kept copies checked by Claude). **Save & Backup:** restored
`BIOL-TT_2026-10-27_14-30-39.db` (GregDeskTop's backups go to its Downloads folder, one per open); 14:33:28 the save
before it landed, 14:33:29 `DatabaseRestore: Restored`; D became byte-identical to that backup; `BIOL-TT (before restore
2026-10-27 14-33).db` beside it passes the integrity check; lock released; TermPoint restarted to the startup chooser
(the "always open the most recent database" preference is off: by design) and the restored database then opened
(14:37:46). The old process stayed alive with no windows (Open issues). **Chooser variant:** restored the 14:24:54 backup
onto D; D then held only `ZZZT-921 D`, as that backup does, and lost `ZZZT-922 D` and `ZZZT 922 D2`, which `BIOL-TT
(before restore 2026-10-27 14-44).db` kept; both pass the integrity check; the database opened in the same process
(14:44:45), no restart and no stuck process.

### M12 — D changed by someone else (item 10, "Save aborted")
*GregSurface; any computer with Python for the script · R2 R3 R4 · ~15 min*
1. GregSurface writer, adds `ZZZT-601 S` unsaved. Run `python tmp/handtest_10a.py colleague <path to FT.db on the share>`.
2. GregSurface: click Save.
3. Open a section editor and try either button: "Finish Editing First" ("Apply or cancel the open edit first."); close the editor.
4. **Save a Copy…**: the picker suggests `FT (recovered).db`; choosing an existing name gives "Choose a New Name". **Expect** "Your Version Was Saved" and TermPoint reopens the current database: it shows "(colleague)" and not `ZZZT-601 S`; the copy has `ZZZT-601 S`.
5. Repeat 1–2 and choose **Discard Changes** → "Discard Changes?" → Discard. **Expect:** reopens, edit gone, "(colleague)" still there.

**Expect (step 2):** a persistent banner, "TermPoint can't save your changes: the database was changed elsewhere since you opened it. Save your version as a separate file, or discard your changes, and TermPoint will reopen the current database.", with only **Save a Copy…** and **Discard Changes**; no Dismiss; no other message replaces it.
**Log:** `source hash mismatch — the database was modified outside this session; save refused.`, then `save conflict — the user's version was saved as a separate file; the kept changes were deleted.` (or `…the user's changes were discarded.`).
**Result:** ☒ pass ☐ fail / notes: 2026-10-22, GregDeskTop, 1.2.3.1, all as expected (user). Log check (Claude):
refused on the hash mismatch 20:47:22; Save a Copy… created `BIOL-TT (recovered).db` and reopened the database
20:48:29; Discard Changes 20:50:09; a second copy went to `BIOL-TT (recovered-1).db`, never over the first, 20:52:04.

---

## Tier 2 — SHOULD

*Why: failures here freeze or strand a user but don't lose or overwrite data, or they test items not built yet. Run after the Musts.*

### S1 — Second copy on the same computer (item 4)
*GregSurface · R3 R4 · ~10 min*
1. Writer with an unsaved edit. Start a second TermPoint on the same database (same Windows user). **Expect:** "TermPoint Is Already Open": "TermPoint is already open on this computer. If it isn't responding, end it in Task Manager and start again — your changes will be offered back."; no Restore prompt; the first window untouched and its edit saves.
2. Freeze the first (Resource Monitor → CPU → right-click TermPoint.exe → Suspend Process). Start a second: the same message. End the first from Details; relaunch: "Restore Unsaved Changes?" → Restore.
3. A killed copy never blocks a relaunch (that is M2).

A second copy CAN open a different database: the guard is per database and per Windows user
(`DatabaseInstanceGuard`), so only the same database is refused.

**Result:** ☒ pass ☐ fail / notes: 2026-10-22, GregDeskTop, 1.2.3.1, local test copies (user; log: guard refusals at 20:02:56, 20:07:28 and 20:10:35): the second copy was refused on the same
database with the expected message and no Restore prompt, and opened a different database, as designed; a frozen first
copy also blocked a second one, and after ending it, Restore was offered.

### S2 — The local copy fails after validation (item 4)
*GregSurface, GregDeskTop, Mekler · R3 R4 · ~15 min*
**Setup:** GregDeskTop is writer on `FT.db`. GregSurface opens it (read-only needs a fresh copy). Clumsy **on GregSurface** black-holes its link during that copy (tune the timing: open, then Drop 100 % ~2 s in), so GregDeskTop is unaffected.
1. **Expect:** "Couldn't Open the Database": "TermPoint couldn't open this database. Check the network connection, then try again." with **Try again** and **Close**. Restore the link, **Try again**: GregSurface opens read-only naming GregDeskTop.
2. Repeat, choose **Close**: nothing is left open on D; GregDeskTop is still writer with its lock untouched; no stray lock or `.tmp` on the share.

**Log:** `read-only setup copy timed out — network unreachable`.
**Result:** ☐ pass ☐ fail / notes:

### S3 — Reader Refresh failures (item 11's messages, built in item 28; the reconnection moved to 1.2.4)
*GregDeskTop (reader), GregSurface (writer), Mekler · R4 · ~15 min*
- **(a) Share down.** Black hole, click **Refresh View**. **Expect** (item 28's approved text): "Couldn't refresh: the database location isn't responding. Showing data from h:mm AM/PM." in the yellow row; the tooltip still shows the last refresh that *worked*; the section list isn't reloaded; network back and Refresh clears that line only.
- **(b) File locked.** Hold GregDeskTop's local snapshot (newest `*_ro*.db` under `%AppData%\TermPoint\`) open with `$f=[IO.File]::Open('<path>','Open','Read','ReadWrite')`, then Refresh. **Expect:** "Couldn't refresh just now. Showing data from h:mm AM/PM. Try again in a moment." (or "TermPoint couldn't refresh and lost its connection to the database. Close TermPoint and open it again." if the swap got as far as closing the connection: record which); the data stays readable; `$f.Close()` and Refresh works.
- No red log-text banner in either case.
**Result:** ☐ pass ☐ fail / notes:

### S4 — Repeated X and edits during the exit save (item 15's exit guard, built 2026-10-26)
*GregSurface, Mekler · R3 · ~15 min*
Degraded link (or the DEBUG switch `TERMPOINT_DEBUG_SLOW_EXIT_SAVE=<seconds>` on a Debug build). Add `ZZZT-701 S`, click **X**, then click X three more times while the exit save runs. Then try to add `ZZZT-702 S` during the save.
**Expect:** a "Saving and closing…" screen covers the window at the first X and blocks input (no edit can be made during the save); detached panels and sticky notes are hidden; the extra X clicks are ignored (one log line each); the window closes when the save ends, or after the 2-minute backstop. `ZZZT-701 S` is in the database at the next open (or, if the save failed, offered back). The edit made *before* the save must never vanish without an offer: that is a fail. (F2's edit counter, for an edit landing mid-save in other ways, is 1.2.4.)
**Result:** ☒ pass ☐ fail / notes: 2026-10-27, GregDeskTop, 1.2.3.2, run with M10 (a) (user; log by Claude). The screen
covered the window at the first X, so no edit could be started; the extra X clicks were ignored (14:12:14–15,
14:15:04, 14:23:08–09, one log line each); the window closed when the save ended (about 50 s at 14:15, 12 s at 14:23).
Every edit made before X reached D, directly or through Restore. Not tried: detached panels and sticky notes, and the
2-minute backstop (`TERMPOINT_DEBUG_SLOW_EXIT_SAVE` on a Debug build).

### S5 — Unapplied editor changes at close and switch (item 14; not built)
*GregSurface · R3 · ~10 min*
For a section, meeting and course editor: type a change, don't Apply, then (a) Files → Exit and (b) switch database. **Expect if built:** Apply / Discard / Cancel; Apply goes through validation; a closed editor never prompts. **Known today:** the change is dropped without a prompt. Record it.
**Result:** ☐ pass ☐ fail / ☐ not built / notes:

### S6 — New database and recovered-copy export with the share down (item 16)
*GregSurface, Mekler · R4 · ~10 min*
Black hole first. (a) File → New, pick a folder on the share. (b) With the M12 banner showing, **Save a Copy…** to a path on the share. (c) Wizard "existing database" via the suggestion click, not Browse….
**Expect:** a network message within seconds, no freeze or "Not responding", and in (b) the banner still there with nothing changed.
**Result:** ☒ pass ☐ fail / notes: 2026-10-22, GregDeskTop, 1.2.3.1. (a) pass: "Cannot reach the database…" (user).
(b) not run by hand (user's decision): Windows' Save dialog cannot show a black-holed share, so the destination can only
be picked before the cut, which is fiddly. Covered by M1 Part A: the export's network steps are the guarded copy and
rename the probe passed 18/18. **Observation (R4, recorded, not changed):** with the share already dead, clicking
Save a Copy… showed nothing for about 45 s: Windows opens its Save dialog in the last folder used (here the share) and
waits for it before appearing. Starting the dialog in a local folder would avoid it but would make normal use worse.
(c) pass: the wizard's path box is read-only (Browse… only), so the file was picked with the network up, then the link
was cut and Next clicked: "can't reach" message, no freeze (user).

### S7 — Remote database via the wizard and the UNC picker (items 1, 18)
*The Windows 10 and the Windows 11 computer · ~20 min*
1. First run (reset settings): wizard → existing database → Browse…, dwell in a UNC picker for > 60 s, pick `\\Mekler\<share>\TermPoint\FT.db`.
2. Files → Open on a remote share, again dwelling > 60 s.
3. Wizard → browse, then **Cancel**.

**Expect:** each opens or cancels without a freeze; no `TermPoint.exe` lingers after Cancel. (The compositor deadlock was Win10-specific.)
**Result:** ☐ pass ☐ fail / notes (Win10, Win11):

### S8 — Windows 11: AutoCompleteBox crash test (items 1, 22)
*The Windows 11 computer or VM · ~10 min*
In the section editor and in the Meetings flyout, click a Start Time suggestion and a Length suggestion; wait 10 s after each. Also run item 1's meeting-time entry checks. **Expect:** no crash.
**Result:** ☐ pass ☐ fail / notes:

### S9 — Cloud-synced folders (item 13; built 2026-10-26)
*A Windows computer that syncs a SharePoint or Teams library through OneDrive · R2 · ~15 min*
The detection reads OneDrive's registry entries, and their layout was never seen on a real library during development, so this is its first real check.
1. In the wizard, point at a database (or a new database folder) inside the synced SharePoint/Teams library (the folder named after the organisation, e.g. `C:\Users\<you>\University of the Fraser Valley\<Site> - Documents`). **Expect:** a warning, not a block: "This folder is kept in sync by OneDrive (a SharePoint or Teams library). Sync services don't respect…".
2. The same inside your OneDrive folder. **Expect:** the same warning naming plain "OneDrive".
3. File → New Database with a folder inside the library. **Expect:** the same warning as step 1.
4. **If step 1 or 3 shows no warning,** run this read-only PowerShell and keep its output for the fix (it changes nothing):
   ```powershell
   $p='HKCU:\Software\SyncEngines\Providers\OneDrive'
   '== SyncEngines'; if(Test-Path $p){Get-ChildItem $p|ForEach-Object{$v=Get-ItemProperty $_.PSPath;"{0} | {1} | {2}" -f $v.MountPoint,$v.LibraryType,$v.UrlNamespace}}else{'(none)'}
   '== Tenants'; Get-ChildItem 'HKCU:\Software\Microsoft\OneDrive\Accounts' -ErrorAction SilentlyContinue|ForEach-Object{$t="$($_.PSPath)\Tenants"; if(Test-Path $t){Get-ChildItem $t|ForEach-Object{"tenant: $($_.PSChildName)"; (Get-Item $_.PSPath).Property|ForEach-Object{"  $_"}}}}
   '== Env'; 'OneDrive','OneDriveCommercial'|ForEach-Object{"$_ = "+[Environment]::GetEnvironmentVariable($_)}
   ```
**Not in 1.2.3 (don't record as failures):** File → Open and Recent show no cloud-folder warning for any provider (1.2.4); Google Drive for desktop, Box and macOS `~/Library/CloudStorage/…` (1.2.4).
**Result:** ☐ pass ☐ fail / notes (steps 1–3; probe output if needed):

### S10 — Section list: editor-recycling data test, and default OFF (item 23)
*GregSurface · ~15 min*
- **ON, 800-section database.** Open an editor, change several fields including a typed Start Time, click the schedule grid, scroll the editing card far out of view and back, **Apply**. **Expect:** every value saved.
- **OFF (default).** The list behaves as before, apart from its own scrollbar and PageDown now working.

**Result:** ☐ pass ☐ fail / notes (ON, OFF):

### S11 — macOS pass (items 4, 9, 13, 19)
*Mac · R3 R4 · ~30 min*
- **Guard (item 4).** A second copy on the same database is refused; a copy ended with `kill -9 <pid>` doesn't block a relaunch (it offers Restore); a frozen copy (`kill -STOP <pid>`) does block, with the message above.
- **Takeover (item 9).** Make the Mac the third computer in M5 (6), or stand in for GregDeskTop in M5 (3).
- **Cloud folders (item 13).** macOS detection (`~/Library/CloudStorage/…`) moved to 1.2.4: a database there gives no warning in 1.2.3; note it, don't record a failure.
- **Dialog layering.** "Database Lock Is Inactive", "Couldn't Open the Database" and the restore prompts appear above the main window ("paints but ignores clicks" means one is hidden).
- **Popup wheel (item 19).** Scroll the mouse wheel past the end of a dropdown: the section list underneath doesn't move.

**Result:** ☐ pass ☐ fail / notes:

---

## Tier 3 — NICE

*Why: opt-in, cosmetic, hardware-dependent or post-release, with no effect on the shared database.*

### N1 — Section-list virtualization ON (item 23), 800-section database
*GregSurface · ~30 min*
Clicks (click, then double-click a mid-list card: the editor opens on it, no jump). Editor (open and close near top, middle and bottom). Keyboard (click a card, arrow Down several times, no drift; PageDown pages). Scrolling away (wheel and scrollbar-drag from a selected card and an edited card: no snap back). Cross-view (select a grid tile whose card is off-screen). Add, Copy, collapse/expand, multi-semester, detached Section View. Toggle the preference while the list is open.
**Result:** ☐ pass ☐ fail / notes:

### N2 — Virtualization: touch and trackpad
*A touchscreen (GregSurface, if it has one), Mac · ~10 min*
A touchscreen flick through a large semester doesn't snag after the finger lifts. macOS trackpad momentum scrolling doesn't snap back.
**Result:** ☐ pass ☐ fail / notes:

### N3 — BugSnag (items 6, 7; built 2026-10-26)
*GregSurface, Mekler · ~15 min · a Release build (release stage "production"), or a Debug build reporting as "development"*
Provoke: a save that times out (M1); a lost lock (M5 (3)); a killed TermPoint, then relaunch (M2); and a backup with the backup folder pointed at a disconnected share. In BugSnag, open each event.
**Expect** (each kind once per run):
- "Save failed: the database location did not respond"; "Write lock lost: taken over by another session" with metadata `newHolder: other`; "The previous session ended without exiting normally" (metadata `count`, `previousVersion`), sent at the relaunch; "Backup skipped: the backup folder was not available".
- **Nothing private anywhere in an event:** no Windows user name, computer name, database or folder name, path or email (paths appear only as tags such as `<drive H: path>` / `<unc path>`, names as `<user>` / `<machine>`); the Device tab has no host name; breadcrumb holders read `self` / `other` / …, never a name.
- The User tab shows the install ID (the same on every event from this computer).
- On screen: the backup failure shows one plain banner per run ("TermPoint couldn't make a backup because the backup folder isn't responding…"), with no path.
**Result:** ☐ pass ☐ fail / notes:

### N4 — Store update link (item 3) — after Store certification, so it can't gate release
*A computer still on 1.2.2 · ~5 min*
Open `ms-windows-store://downloadsandupdates`: "Get updates" finds TermPoint 1.2.3. Note whether the product page (`ms-windows-store://pdp/?PFN=Academic-Solutions.TermPoint_9nmm6ygzk390e`) then shows "Update".
**Result:** ☐ pass ☐ fail / notes:

---

## Suggested running order

Each block reuses one setup. Times include resets and the 4-minute sleeps.

| # | Block | Scenarios | Computers | ~Time |
|---|---|---|---|---|
| 0 | Probe (any time, no one else needed) | M1 Part A | GregDeskTop | 40 min |
| 1 | One writer, clumsy on Mekler | M1 Part B, M2 + M3, M10, M12, S6, M11 (a) | GregSurface, Mekler | 2 h |
| 2 | Two users | M9, M4, M11 (c)(d), then M11 (b) last | GregSurface, GregDeskTop, Mekler | 1 h 15 |
| 3 | Two users, laptop sleeps | M5 (1)–(5), (7), M6; (6) only if a third computer is free | GregSurface, GregDeskTop, Mekler (+ third) | 2 h |
| 4 | One computer | M7, S1, S5, S10 | GregSurface | 45 min |
| 5 | Mixed versions (1.2.2 on GregSurface, then reinstall 1.2.3) | M8 | GregDeskTop, GregSurface, Mekler | 40 min |
| 6 | Degraded opens | S2, S3, S4 | GregSurface, GregDeskTop, Mekler | 45 min |
| 7 | Windows UI | S7, S8, S9 | the Win10 and Win11 computers | 45 min |
| 8 | Mac | S11 | Mac | 30 min |
| 9 | Any time | N1, N2, N3 | GregSurface | 55 min |
| 10 | After Store certification | N4 | a computer on 1.2.2 | 5 min |

Blocks 0–5 hold every Must plus the cheap Shoulds that share their setup (~7 h 20; the Musts alone are ~6 h with resets).
Block 5 comes before the Should blocks so 1.2.3 is back on GregSurface for them. **Short on time (half a day):** M1 (Part A
full run, one Part B cut), M2, M3,
M5 (1)–(3), M6, M7, M12, M8 Dir 3. Run the rest of the Musts before release, not after.

## Wrap-up

- [ ] Collect every computer's log; mark each fail or odd result with the time, the computer and the log line.
- [ ] Any message that said "safe", "working copy", "local copy", or showed a path or log text: list the exact text.
- [ ] All Must results are pass, or each fail has a decision (fix, or accept with a release note).
