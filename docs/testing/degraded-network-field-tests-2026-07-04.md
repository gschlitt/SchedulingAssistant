# Degraded-Network Field Test Protocol — 2026-07-04

Field validation for today's persistence-layer fixes. Three scenarios, in order:
**A** — black-hole mid-save (freeze-fix retest), **B** — two-instance contention,
**C** — kill-the-process mid-save (crash recovery).

## Topology

| Machine | Role |
|---|---|
| **GREGDESKTOP** | TermPoint instance #1 — the **writer** in every scenario |
| **GREGSURFACE** | TermPoint instance #2 — the **reader** (Scenario B) |
| **MEKLER** | Hosts the database share (`\\MEKLER\<share>\TermPoint\GEOG-TT.db`). No TermPoint runs here — it also runs **clumsy** |

Clumsy on MEKLER with filter `inbound or outbound` degrades **both** clients' access
to the share, which is what every scenario below wants. (If you ever need to degrade
just one client, filter by that client's IP: `ip.SrcAddr == <ip> or ip.DstAddr == <ip>`.)

Sanity anchor: the lock file's `Machine` field names the **writer instance's** machine
— it should only ever read `GREGDESKTOP` or `GREGSURFACE`. If `MEKLER` ever appears as
a lock holder, something is running where it shouldn't be.

## Fixes under test (all landed 2026-07-04)

| Fix | What changed | Field signal |
|---|---|---|
| Contended lock read (save path) | Sharing violation on D.lock → transient, no demotion | log: `lock read contended … keeping writer state` |
| Reader poll false-availability | Unreadable lock during poll → skip cycle, no takeover offer | log: `Poll: lock read contended … skipping cycle` |
| Chunk-size throughput floor | 1 MB → 64 KB chunks; slow links no longer misclassified | saves succeed slowly instead of timing out |
| Abandoned-task observation | Late black-hole faults logged, never UnobservedTaskException | log: `abandoned … task faulted after timeout` |
| UI-thread freeze (ConfigureAwait / bounded closes) | Network disposes off the UI thread, deadline-bounded | **no** "Not responding", banner ≤ ~10 s |

## Setup (once)

1. **Deploy the new build to GREGDESKTOP and GREGSURFACE — via MSIX.** Rebuild the
   sideload package from TODAY's source first (dev-console task 01) — sideloading a
   previously-built package silently tests the old binary; the `v1.1.4.0` stamp will
   NOT warn you (assembly version was set manually). MEKLER needs nothing installed.
   Sanity check: the package build timestamp must be after ~15:30 2026-07-04.
   GS:OK
   
2. **MSIX runs detached from VS — good** (no debugger artifacts). If you ever rerun
   from VS instead, use Ctrl+F5: Just-My-Code breaks on the late faults of observed
   abandoned tasks at throw time — a debugger artifact, not a production crash.
   GS:OK
   
3. **Clean baseline:** close TermPoint everywhere; in `\\MEKLER\<share>\TermPoint\`
   confirm no leftover `GEOG-TT.lock`, `GEOG-TT.db.tmp`, or timestamped
   `GEOG-TT_*.lock` files. From GREGDESKTOP do one normal open → edit → save → close
   cycle to confirm health before starting.
   GS:OK
   
4. **Find and watch the LIVE log on GREGDESKTOP and GREGSURFACE** (MEKLER has none).
   Under MSIX, AppData writes may go to the real Roaming folder or to the package
   container — it varies. Don't assume: launch the app, do one save, then run this on
   each client machine — it picks whichever candidate file is actually growing:
   ```powershell
   $today = Get-Date -Format yyyy-MM-dd
   $live = @(
     "$env:APPDATA\TermPoint\Logs\app-$today.log",
     "$env:LOCALAPPDATA\Packages\AcademicSolutions.TermPoint_d3gb38k4yrz2r\LocalCache\Roaming\TermPoint\Logs\app-$today.log"
   ) | Where-Object { Test-Path $_ } |
       Sort-Object { (Get-Item $_).LastWriteTime } -Descending | Select-Object -First 1
   "Watching: $live"
   Get-Content $live -Wait -Tail 5
   ```
   The `Watching:` line tells you which location won — note it once per machine and
   collect from there at wrap-up. (The package-family hash `d3gb38k4yrz2r` is derived
   from the package identity, so it is the same on both machines.)
   GS:
   On GREGDESKTOP this is  C:\Users\gregs\AppData\Local\Packages\AcademicSolutions.TermPoint_d3gb38k4yrz2r\LocalCache\Roaming\TermPoint\Logs\app-2026-07-04.log
   On GREGSURFACE this is C:\Users\gregs\AppData\Local\Packages\AcademicSolutions.TermPoint_d3gb38k4yrz2r\LocalCache\Roaming\TermPoint\Logs\app-2026-07-04.log
   
   
   
5. **Clumsy on MEKLER**, filter `inbound or outbound`. "Degraded" below =
   Lag 80–150 ms + Drop 3–5 %. "Black hole" = Drop 100 % (both directions).
6. Autosave ON (on the writer), and note the configured autosave interval
   (Preferences) — some waits below reference it.
   
GS: Autosave on on GREGDESKTOP (writer) with interval 10 minutes

Record each step as **PASS / FAIL / odd** with the wall-clock time and which machine;
note the log line if odd. Timing constants behind the expectations: heartbeat renewal
60 s, reader poll 60 s, stale-lock threshold 180 s, per-op stall deadline 5 s.

---

## Scenario A — Black hole mid-save (freeze-fix retest)

**GREGDESKTOP only** (GREGSURFACE closed). The point is to open the hole at
**different moments** in the ~15 s save window, because the old bug only fired when
the hole opened *late*.

Under **degraded** settings (so saves are slow enough to aim at), make an edit, click
Save, and flip Drop to 100 % on MEKLER at the target moment. Three timings × 2
repetitions:

| # | Flip drop to 100 %… | Expected |
|---|---|---|
| A1 | ~2 s into the save (early — mid-copy) | Banner ≤ ~10 s. Log: `CopyFile (read)`/`(write)` stall |
| A2 | ~8 s in (mid — copy/verify boundary) | Banner ≤ ~10 s. Log: copy or `ComputeHash` stall |
| A3 | ~13–14 s in (late — the old freeze case) | Banner ≤ ~10 s. Log: `CopyFile (close)` or rename stall |
GS:

A1: approx 19:50
- [ok ] UI stays responsive throughout — no "Not responding", window drags/redraws fine
- [ok ] "Cannot reach the database…" (or equivalent transient) banner within ~10 s
- [ ok] No exception dialog, no red `UnobservedTaskException` banner
- [ ok] ~30–60 s later the log shows quiet `abandoned … task faulted after timeout` lines
- [ ok] Still in **write mode** (not demoted to reader)
- [ ok] Restore the network (Drop back to 0) → next save (manual or autosave) **succeeds**  Note: succeeds, but does not say "...saving.." in the interim before the save comes through. A couple clicks necessary
- [ok ] After the successful save, close app; reopen; the edit is present

A1: approx 19:56
- [ok ] UI stays responsive throughout — no "Not responding", window drags/redraws fine
- [ok ] "Cannot reach the database…" (or equivalent transient) banner within ~10 s
- [ ok] No exception dialog, no red `UnobservedTaskException` banner
- [ ok] ~30–60 s later the log shows quiet `abandoned … task faulted after timeout` lines
- [ ok] Still in **write mode** (not demoted to reader)
- [ ok] Restore the network (Drop back to 0) → next save (manual or autosave) **succeeds** Note: succeeds, but does not say "...saving.." in the interim before the save comes through. A couple clicks necessary
- [ok ] After the successful save, close app; reopen; the edit is present

A2: approx 20:00

- [ok ] UI stays responsive throughout — no "Not responding", window drags/redraws fine
- [ok ] "Cannot reach the database…" (or equivalent transient) banner within ~10 s
- [ ok] No exception dialog, no red `UnobservedTaskException` banner
- [ ok] ~30–60 s later the log shows quiet `abandoned … task faulted after timeout` lines
- [ ok] Still in **write mode** (not demoted to reader)
- [ ok] Restore the network (Drop back to 0) → next save (manual or autosave) **succeeds** Note: succeeds, but does not say "...saving.." in the interim before the save comes through. A couple clicks necessary
- [ok ] After the successful save, close app; reopen; the edit is present

A2: approx 20:05

- [ok ] UI stays responsive throughout — no "Not responding", window drags/redraws fine
- [ok ] "Cannot reach the database…" (or equivalent transient) banner within ~10 s
- [ ok] No exception dialog, no red `UnobservedTaskException` banner
- [ ok] ~30–60 s later the log shows quiet `abandoned … task faulted after timeout` lines
- [ ok] Still in **write mode** (not demoted to reader)
- [ ok] Restore the network (Drop back to 0) → next save (manual or autosave) **succeeds** 
- [ok ] After the successful save, close app; reopen; the edit is present

A3: approx 20:09

- [ok ] UI stays responsive throughout — no "Not responding", window drags/redraws fine
- [ok ] "Cannot reach the database…" (or equivalent transient) banner within ~10 s
- [ ok] No exception dialog, no red `UnobservedTaskException` banner
- [ ok] ~30–60 s later the log shows quiet `abandoned … task faulted after timeout` lines
- [ ok] Still in **write mode** (not demoted to reader)
- [ ok] Restore the network (Drop back to 0) → next save (manual or autosave) **succeeds** 
- [ok ] After the successful save, close app; reopen; the edit is present

A3: approx 20:12

- [ok ] UI stays responsive throughout — no "Not responding", window drags/redraws fine
- [ok ] "Cannot reach the database…" (or equivalent transient) banner within ~10 s
- [ ok] No exception dialog, no red `UnobservedTaskException` banner
- [ ok] ~30–60 s later the log shows quiet `abandoned … task faulted after timeout` lines
- [ ok] Still in **write mode** (not demoted to reader)
- [ ok] Restore the network (Drop back to 0) → next save (manual or autosave) **succeeds** 
- [ok ] After the successful save, close app; reopen; the edit is present


**FAIL =** any freeze, any unhandled-exception dialog, a UTE banner, a demotion to
reader, or a post-recovery save that doesn't land.

---

## Scenario B — Two-instance contention

Writer = **GREGDESKTOP** (first to open). Reader = **GREGSURFACE** opening the same
DB on MEKLER. Run B1 → B4 in order; B5 is a bonus.

### B1 — Reader polls under degradation while writer is alive (today's poll fix)
1. GREGDESKTOP open and idle as writer. Start GREGSURFACE → it must open
   **read-only**, banner naming `gregs@GREGDESKTOP` as the holder.
   GS:OK
   
2. Enable **degraded** clumsy on MEKLER. Leave both instances sitting **≥ 6 minutes**
   (≥ 5 reader poll ticks). Meanwhile make an edit + save on GREGDESKTOP every couple
   of minutes so the heartbeat rename and the reader's poll get chances to collide.
- [ ] GREGSURFACE **never** shows a "switch to edit mode" / write-access-available offer
- [ ] GREGSURFACE log may show `Poll: lock read contended … skipping cycle` — that's
      the fix working (absence is also fine; it means no collision happened)
- [ ] GREGDESKTOP stays writer; saves keep working (slowly — its link is degraded too)
	GS:started about 20:48
	
	Around 20:50  tried 'Save' a few times, system immediately returned 'Unsaved Changes' (not an attempt to follow through on the save)
	Then received multiple CheckOutService D.temp copy failed 
	Tried save again: received "Failed to write to database location: The process cannot access the file '\\Mekler\temp\GEOG-TT.db.tmp' because it is being used by another process, also CheckoutService: D.temp copy failed
	Another save: same result.
	
	Even after restoring proper network functioning via clumsy, the same results.
	
	Typical stack trace during this time:
	
[2026-07-04 20:53:05.165] [ERROR] [v1.1.4.0]
  Context : CheckoutService: D.tmp copy failed
  Type    : System.IO.IOException
  Message : The process cannot access the file '\\Mekler\temp\GEOG-TT.db.tmp' because it is being used by another process.
  Stack   :
       at System.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable`1 unixCreateMode)
       at System.IO.FileStream..ctor(String path, FileMode mode, FileAccess access, FileShare share, Int32 bufferSize, FileOptions options)
       at TermPoint.Services.NetworkFileOps.<>c__DisplayClass6_0.<CopyAsync>b__0() in C:\Users\gregs\source\repos\SchedulingAssistant\src\TermPoint\Services\NetworkFileOps.cs:line 175
       at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
    --- End of stack trace from previous location ---
       at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
       at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
    --- End of stack trace from previous location ---
       at TermPoint.Services.NetworkFileOps.CopyAsync(String source, String dest) in C:\Users\gregs\source\repos\SchedulingAssistant\src\TermPoint\Services\NetworkFileOps.cs:line 184
       at TermPoint.Services.CheckoutService.SaveAsyncCore(Boolean releaseLockAfter, Boolean isAutoSave) in C:\Users\gregs\source\repos\SchedulingAssistant\src\TermPoint\Services\CheckoutService.cs:line 977
	
	
	
**FAIL =** any takeover offer on GREGSURFACE while GREGDESKTOP is alive. (Pre-fix, one
collision produced a permanent false offer.)



New Trial: 21:21
Save mostly don't go through, but when they fail I receive the correct banner. The time between a save-click and the banner can vary quite a  bit
Restoring proper network conditions restores saving. 
Also tried "Refresh View" on GREGSURFACE a few times, that seemed to function.




### B2 — Writer exits gracefully → reader offered write access
1. Clumsy OFF. Close GREGDESKTOP normally (final save + lock release).
2. Watch GREGSURFACE.
- [ ] Within ~60 s (one poll tick): GREGSURFACE offers write access
      (log: `Poll: lock file gone — write access available`)
- [ ] Accept → GREGSURFACE becomes writer; make an edit + save → succeeds
- [ ] Relaunch GREGDESKTOP → it opens **read-only**, naming `gregs@GREGSURFACE`


GS:OK




### B3 — Writer crashes → stale-lock takeover
1. Reset roles: close both, reopen GREGDESKTOP first (writer), GREGSURFACE second
   (reader). Make an **unsaved edit** on GREGDESKTOP, then HARD-kill it:
   `taskkill /F /IM TermPoint.exe` (the `/F` is essential) or Task Manager →
   **Details tab** → End task. Do NOT use the Processes-tab "End task" — that sends
   WM_CLOSE first, so the app gracefully saves and releases the lock (which is the
   outcome-checked shutdown working, but it isn't a crash). After a true hard kill
   the lock file MUST still be on MEKLER — verify that before proceeding.
2. Watch GREGSURFACE.
- [ ] For the first ~3 min GREGSURFACE stays read-only (heartbeat not yet stale).
      Cross-machine it cannot check the holder's PID, so it MUST wait out the
      heartbeat threshold — this delay is by design
- [ ] Between ~3–4 min (180 s threshold + ≤ 60 s poll): GREGSURFACE reports the lock
      stale (log: `Poll: lock is stale (age ~180+s)`) and offers takeover
- [ ] Accept → GREGSURFACE is writer, can edit + save
- [ ] Relaunch GREGDESKTOP → **crash-recovery path**: it must NOT silently become
      writer; expect read-only (GREGSURFACE holds the lock) **plus** recovery handling
      for the unsaved D′ edit (Restore/Discard or export offer — record what it
      shows). No corruption; the DB opens.
	
GS: When GREGDESKTOP opened it offered to restore edits, but then observed that somebody else was writer (GREGSURFACE) and said I could do next time I was writer. That is correct.
In a second scenario, I killed GREGDESKTOP after an edit. When I took over as writer on GREGSURFACE I made an edit and saved.  When I started up GREGDESKTOP again I was not offered a chance to restore edit. Also correct.

### B4 — Same-machine dead-PID auto-reclaim (control)
1. Clean state: close both instances, delete nothing. Open GREGDESKTOP as writer.
   Kill it (as B3). **Keep GREGSURFACE closed.** Relaunch on GREGDESKTOP.
- [ ] It auto-reclaims silently (log: `Reclaimed lock from dead session (PID …)`) and
      is writer immediately — no 3-minute wait, no prompt about the lock itself.
      (Same machine = it CAN check the dead PID, unlike B3)
- [ ] Dirty-edit recovery prompt appears if you left unsaved edits (expected)

GS:OK for B4


### B5 (bonus) — B1 repeated with the writer mid-save
Same as B1, but time GREGSURFACE's poll window while GREGDESKTOP is inside a long
degraded save. This maximizes heartbeat/poll/save collisions on MEKLER's copy of
D.lock. Same pass criteria as B1.

GS: Did this already, seemed ok.
---

## Scenario C — Kill the process mid-save (crash recovery)

The power-cut case, on **GREGDESKTOP** (GREGSURFACE closed). Use **degraded** clumsy
to stretch the save to ~15 s so the kill is aimable. All kills must be HARD kills:
`taskkill /F /IM TermPoint.exe` (with `/F`), Task Manager **Details tab** → End task,
or `Stop-Process -Name TermPoint -Force`. The Processes-tab "End task" and taskkill
without `/F` send WM_CLOSE → the app saves gracefully and releases the lock — that
tests shutdown, not a crash (as discovered in B3).

### C1 — Kill during the copy (early, ~3 s into the save)
1. Make a distinctive edit (e.g. add section `ZZZT-999 A`). Click Save. Kill at ~3 s.
2. Check `\\MEKLER\<share>\TermPoint\`: a `GEOG-TT.db.tmp` may linger — note it.
3. Clumsy OFF. Relaunch on GREGDESKTOP.
- [ ] Recovery prompt appears (unsaved-changes Restore/Discard) — D was **not**
      updated, so Restore must be offered
- [ ] Choose Restore → the `ZZZT-999 A` edit is present; save succeeds; DB healthy
- [ ] Any leftover `.tmp` is swept (or at minimum harmless — no error, no corruption)

GS: No .tmp file On Mekler. Everything  worked.


### C2 — Kill during verify/rename (late, ~13 s into the save)
1. Same drill, kill at ~13–14 s. This straddles the ghost-rename window: the save may
   or may not have actually landed on MEKLER.
2. Clumsy OFF. Relaunch.
- [ ] **Either** outcome is a pass, as long as it's honest:
      (a) D already contains the edit (rename landed) → app recognizes it (pending-save
      hash / ghost-rename heal), no false "modified outside this session" conflict; or
      (b) D unchanged → Restore/Discard offered, Restore recovers the edit
- [ ] The DB passes integrity (opens cleanly, section counts sane) in both cases

**FAIL =** the edit is silently lost with no recovery offer, a corrupted/unopenable DB,
a false external-modification conflict against our own half-landed save, or a stuck
lock that B4's auto-reclaim doesn't clear.

GS: looks good. Tried it twice.

### C3 (control) — Kill while idle and clean
1. Open, save, let it sit idle (no unsaved edits). Kill.
2. Relaunch.
- [ ] No recovery prompt (nothing was at stake), auto-reclaim, straight to work


GS:looks good



## Wrap-up checklist

- [ ] Collect both client logs for the session (GREGDESKTOP + GREGSURFACE, from the
      locations the setup snippet reported)
- [ ] Note every FAIL/odd with timestamp and machine → we fix before sign-off
- [ ] All pass → the persistence layer is field-validated for: slow, lossy, severed,
      contended, and crashed — the realistic failure envelope for a departmental
      network share
