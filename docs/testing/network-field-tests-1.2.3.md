# TermPoint 1.2.3 — network field tests

Hand-run checklist, essential first. It re-runs the July protocol
([degraded-network-field-tests-2026-07-04.md](degraded-network-field-tests-2026-07-04.md): A, B1–B5, C1–C3) against the
1.2.3 release build and adds the 1.2.3 scenarios of the spec's "Release testing: network field tests"
(`spec_version_1.2.3.md`) and the fixes F1, F4, F8 and F12 ([review §3](../../designs/data-persistence-review-2026-10.md)).
**Do the Must tier before release; Should and Nice only after.** Several Should/Nice scenarios test items that
are not built yet (11, 13, 14, 15, 6, 7); each says what to record if so.

## Current work list (from the network probe, 2026-10-22)

Timed black holes from Mekler proved unreliable for M1 (a miss looks like a pass). Network-outage behaviour is now
tested with `tools/NetworkProbe` (spec: `designs/network-probe-spec.md`), which self-checks its own cut.

- [x] **A1 found and fixed:** a cut mid-copy could go unnoticed for the whole outage (no log line, no banner) because
      the copy's chunk write could block before its 5 s timer started. Fix: the write runs under `Task.Run`
      (`NetworkFileOps.CopyCoreAsync`). Probe before: 1 of 3 mid-copy runs failed; after: 10 of 10 passed. Full test
      suite green. Committed b193eb8.
- [x] **1. The hash's reads:** same pattern (`NetworkFileOps.ReadChunkAsync`); reads now start under `Task.Run`; full
      suite green. The probe's `hash-cm` now cuts only after 1 MB has provably been read and counts a pass only if
      caught in `ComputeHash (read)`. Probe 2026-10-22 18:58: hash-cm 5 of 5 caught in the read; copy-cm 5 of 5 (15 of
      15 since the write fix). Committed b193eb8.
- [x] **2. New MSIX test build** 1.2.3.1 with both fixes: installed on GregDeskTop 2026-10-22; the log stamps
      `[v1.2.3.1]` (the window title shows only three parts, "v1.2.3"). Install it on GregSurface too (certificate
      first, see "Before you start"); never on Mekler.
- [ ] **Before release:** set `<Version>` back to `1.2.3` in `src/TermPoint/TermPoint.csproj`; never commit 1.2.3.1.
- [x] **3. Checklist updated:** M1 is now Part A (the probe, every network call) plus Part B (a few untimed cuts in real
      TermPoint, to see the banner and recovery); M2 kills by watching "Saving…", not by the second; A1 recorded.
- [x] **M1 Part A:** full probe run on the final code, 2026-10-22 19:35: 18 of 18 passed, controls OK.
- [ ] **Watch for:** the database chooser panel staying on screen after a database opens (seen once on GregSurface,
      1.2.3.1, 2026-10-22; could not make it happen again). If it does: do not press its buttons; drag its header, then
      minimise and restore the main window (does it come back with it?), and note the Windows version (`winver`).
- [ ] **Watch for / probe:** a TermPoint process left running with no window after its main window closed (seen once,
      GregDeskTop, 2026-10-22: PID 15416 closed its main window at 21:44:48 during the restart after a restore, logged
      "Managed teardown complete" but never "Dispatcher loop exited", and kept two hidden untitled windows). It held no
      lock or guard. Suspected: the hidden-not-closed database chooser or wizard refusing the app's own shutdown. Probe:
      open via the chooser, close with the X, check Task Manager for a leftover TermPoint.exe.
- [ ] **4. Later:** `BackupService` deletes a partial backup with plain, unguarded file calls when the backup folder's
      share is dead (`BackupService.cs:286`); see `tools/NetworkProbe/GAP-AUDIT.md` for the other 24.
- [x] **Committed** 2026-10-22: b193eb8 (the fix), d06b8d0 (probe, spec, this checklist). Not pushed.

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
  with Inbound and Outbound ticked, then press Start. *Degraded* = Lag 80–150 ms + Drop 3–5 %. *Black hole* = Drop 100 %.
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
whole outage, with no log line, no banner and "Saving…" until the network came back. It is fixed (work list above). So
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

**Result:** ☐ pass ☐ fail / notes (B1, B2, B3):

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
4. **(4) Nobody changed D.** As (3), but GregDeskTop takes over and closes without edits. GregSurface restarts: Restore. **Expect:** its change restores into D. **Known (review F5):** a session that closes without edits can still rewrite D, which turns this into "Can't Restore" → Save a Copy…. Either way the work is offered back: record which.
5. **(5) Reader waits.** GregSurface asleep 4 min, GregDeskTop opens → **Open read-only**. Open GregSurface's lid, let it resume, then exit it cleanly. **Expect:** GregDeskTop's **Take over as writer** appears within ~60 s and works.
6. **(6) No double takeover (needs a third computer; skip and say so if you have none).** GregSurface asleep 4 min. GregDeskTop opens (prompt, leave it open), then the third computer opens (prompt too). GregDeskTop takes over; then the third computer clicks Take over. **Expect:** it gets "Database Is In Use Again" naming GregDeskTop and is read-only; never two writers (check the lock file's holder and each banner).
7. **(7) Clock skew.** Turn off auto time sync on GregSurface and set its clock 10 min **behind**; it stays active as writer. GregDeskTop opens. **Expect:** a prompt may show (the clock is only a hint), but Take over ends in "Database Is In Use Again" and GregSurface never loses the lock. Then its clock 10 min **ahead**, hard-killed: GregDeskTop's polling finds the dead lock after ~180 s. **Restore the clock afterwards.**

**Result:** ☐ pass ☐ fail / notes ((1)…(7); which computer was the third):

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
**Result:** ☐ pass ☐ fail / notes (was the window hit?):

### M7 — F4: an editor open across a lost lock
*GregSurface; any computer with Python for the script · R3 R4 · ~10 min*
**Setup:** GregSurface writer on a copy of `FT.db`.
1. Open a section's editor and change a field. Do **not** Apply.
2. Run `python tmp/handtest_lock.py takeover <path to the copy>` (Alice takes the lock; if it reports a sharing error, run it again).
3. Wait for the lock-lost banner (≤ ~60 s, or click Save to provoke it): "Write access was taken over by Alice on ALICE-PC. …". GregSurface is read-only; the editor is still open.
4. In the still-open editor click **Apply**.
5. Cancel the editor. Optionally try another write (an Instructors note, focus-loss save).
6. Close TermPoint; `python tmp/handtest_lock.py clear <copy>`; reopen: the change isn't in the database.

**Expect:** exactly **one** message, "You no longer have write access, so this change wasn't saved."; no second banner, no red or log-text banner, nothing written.
**Log:** `heartbeat verify — lock genuinely lost; demoting.`, no error entry for the refused write.
**Result:** ☐ pass ☐ fail / notes:

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
**Result:** ☐ pass ☐ fail / notes:

### M10 — Exit save on a slow or dead link
*GregSurface, Mekler · R3 · ~15 min*
- **(a) Degraded.** Add `ZZZT-501 S`, click **X** once. **Expect:** "Saving…" in the footer, the window closes after the save (≤ ~1 min), the lock is gone (log `Released write lock: …`, `[Shutdown] Managed teardown complete; closing main window.`); reopen: the edit is present, no prompt.
- **(b) Black hole.** Add `ZZZT-502 S`, set Drop to 100 %, click **X** once. **Expect:** after the stall, "Changes Not Saved": "Your latest changes could not be saved to the database. They have been kept, and TermPoint will offer to restore them the next time you open this database." Network back, relaunch: "Restore Unsaved Changes?" → Restore → the edit is present and saves.
- Repeated X and edits during the exit save are S4.

**Result:** ☒ pass ☐ fail / notes (a, b): 2026-10-22, GregDeskTop, 1.2.3.1, both parts as expected (user). Log check
(Claude): (a) exit save landed 20:42:05, lock released 20:42:06. (b) exit save refused at its first check 20:44:13
("lock verification timed out"), lock deliberately left in place ("Suspended"), changes kept; two launches while the link
was still dead stopped at the integrity check without touching the kept changes; at 20:44:51 the relaunch reclaimed its own
lock at once, Restore was offered, and the restored edit saved at 20:45:00.

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
banner's Discard Changes worked and the database reopened (21:27:48). (b), (c), (d): block 2.

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

### S3 — Reader Refresh failures (item 11; not built, plan approved)
*GregDeskTop (reader), GregSurface (writer), Mekler · R4 · ~15 min*
- **(a) Share down.** Black hole, click **Refresh View**. **Expect:** "Couldn't refresh — the database location isn't responding. Showing data from h:mm AM/PM."; the tooltip shows the last *successful* refresh; network back and Refresh clears the message.
- **(b) File locked.** Hold GregDeskTop's local snapshot (newest `*_ro*.db` under `%AppData%\TermPoint\`) open with `$f=[IO.File]::Open('<path>','Open','Read','ReadWrite')`, then Refresh. **Expect:** "Couldn't refresh just now. Showing data from … Try again in a moment."; the data stays readable; `$f.Close()` and Refresh works.

If item 11 isn't in the build: today a failure is only logged and the tooltip claims a refresh just happened. Record what you see.
**Result:** ☐ pass ☐ fail / ☐ not built / notes:

### S4 — Repeated X and edits during the exit save (item 15; not built)
*GregSurface, Mekler · R3 · ~15 min*
Degraded link. Add `ZZZT-701 S`, click **X**, then click X three more times while "Saving…" shows. Separately: during the exit save, add `ZZZT-702 S` (the window stays editable).
**Expect if built:** extra clicks ignored, a clear "saving and closing" state, no edit lost. **Known today:** a second X ends the process mid-save and the next launch offers Restore (or the copy offer if the rename landed); an edit made during the save is deleted with its working files (F2). The edit made *before* the save must never vanish without an offer: that is a fail either way.
**Result:** ☐ pass ☐ fail / ☐ not built / notes:

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

### S9 — Cloud-synced folders (item 13; not built)
*GregSurface, Mac · R2 · ~15 min*
Put a copy of `FT.db` in each of: OneDrive, a SharePoint/Teams library synced through OneDrive, Google Drive for desktop, Box, and (Mac) `~/Library/CloudStorage/…`. Open it through the wizard and File → Open. **Expect:** a clear warning (not a block) that the folder is synced and why that is a risk. **Known today:** OneDrive and Google Drive are detected in some forms; SharePoint, Box and macOS are the gaps. Record each provider.
**Result:** ☐ pass ☐ fail / ☐ not built / notes (per provider):

### S10 — Section list: editor-recycling data test, and default OFF (item 23)
*GregSurface · ~15 min*
- **ON, 800-section database.** Open an editor, change several fields including a typed Start Time, click the schedule grid, scroll the editing card far out of view and back, **Apply**. **Expect:** every value saved.
- **OFF (default).** The list behaves as before, apart from its own scrollbar and PageDown now working.

**Result:** ☐ pass ☐ fail / notes (ON, OFF):

### S11 — macOS pass (items 4, 9, 13, 19)
*Mac · R3 R4 · ~30 min*
- **Guard (item 4).** A second copy on the same database is refused; a copy ended with `kill -9 <pid>` doesn't block a relaunch (it offers Restore); a frozen copy (`kill -STOP <pid>`) does block, with the message above.
- **Takeover (item 9).** Make the Mac the third computer in M5 (6), or stand in for GregDeskTop in M5 (3).
- **Cloud folders (item 13).** `~/Library/CloudStorage/…` warns (S9).
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

### N3 — BugSnag (items 6, 7; 6 not started, 7 accepted)
*GregSurface, Mekler · ~15 min*
Provoke a handled save failure (M1), a lost lock (M5 (3)) and "previous session ended uncleanly" (M2, relaunch). **Expect if built:** the events arrive, with usernames and paths scrubbed. Record whether anything arrives.
**Result:** ☐ pass ☐ fail / ☐ not built / notes:

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
