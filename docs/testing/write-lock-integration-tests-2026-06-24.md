# Write-Lock Integration Test Plan

> Generated 2026-06-24.  
> Companion to `write-access-loss-agenda-2026-06-24.md`.  
> Validates W1 (heartbeat verification) and W2 (cause-aware banners) in a real
> multi-machine, shared-network environment.

---

## Setup

- **Machine A** — the initial writer.
- **Machine B** — the second user / contender.
- Create a shared folder on one machine (or a NAS). Place a TermPoint `.db` file in it.
  Both machines reach it by UNC path (e.g. `\\MACHINE-A\share\schedule.db`).

---

## Group 1 — Happy path (baseline)

### 1. A opens as writer, B opens as reader

- A opens the shared DB → gets write controls.
- B opens the same DB → gets read-only banner **naming A** (username + machine).
- Verify B's banner is specific, not generic.

### 2. A saves, B refreshes

- A makes edits and saves.
- B clicks Refresh → verify B sees A's changes.

### 3. A exits cleanly, B reopens

- A exits TermPoint.
- B reopens → verify B now gets write access (lock released).

---

## Group 2 — Takeover after crash

### 4. Stale-lock takeover after crash

- A opens as writer. B opens as reader.
- Kill A's TermPoint process (Task Manager → End Process — not a clean exit).
- Wait ~3 minutes for the lock to go stale.
- B should be offered takeover. Accept it → B gets write access.
- Relaunch TermPoint on A → A should see a reader banner naming B.

### 5. Stale-lock takeover after crash — B opens fresh

- A opens as writer. B is **not** running.
- Kill A's TermPoint process.
- Wait ~3 minutes. B opens the DB for the first time.
- B should see the stale lock and be offered takeover.
- After taking over, verify B has full write access.

---

## Group 3 — Lock file removal (W2 — LockFileRemoved reason)

### 6. Manual lock-file deletion

- A opens as writer.
- On B (or on A via Explorer), navigate to the share and manually delete the `.lock` file.
- Within ~60–180 seconds A should see:

  > "The database lock file was removed by another program (possibly antivirus or
  > cloud sync software). Any unsaved changes since your last save have been
  > discarded. You can regain write access by exiting and restarting TermPoint."

- Verify the wording is the `LockFileRemoved` variant, **not** the `TakenOver` variant.

---

## Group 4 — Network interruption (W1 fix)

### 7. Network drop — no false demotion

- A opens as writer.
- Disconnect A from the network (disable Wi-Fi / unplug ethernet).
- Wait 3+ minutes (heartbeat failures accumulate).
- Observe: A should show a transient "Network connection interrupted" warning but
  **should NOT demote to read-only**. This is the W1 fix.

### 8. Network restored after drop

- Continue from #7. Reconnect A to the network.
- The transient warning should clear after the next successful heartbeat (~60s).
- A should still have write access. Verify by saving — the save should succeed.

### 9. Network drop + takeover by B (core W2 TakenOver test)

- A opens as writer, makes unsaved edits. Disconnect A.
- While A is disconnected, B opens and takes over the lock on the share
  (A's heartbeat goes stale after ~3 min; B sees it and accepts takeover).
- Reconnect A.
- Within ~60s A should detect the takeover and show the W2 `TakenOver` banner:

  > "Write access was taken over by [B's username] on [B's machine]. Any unsaved
  > changes since your last save have been discarded. You can regain write access
  > by exiting and restarting TermPoint."

- Verify the username and machine name are correct.
- Verify A is in read-only mode (write controls disabled).
- Verify A shows the **last-saved** state, not the unsaved edits.

---

## Group 5 — Sleep/wake

### 10. Sleep and wake — lock still ours

- A opens as writer.
- Sleep A's machine (close lid, or `rundll32 powrprof.dll,SetSuspendState 0,1,0`).
- Wait 4+ minutes. Wake A.
- A should run the wake-check. If the lock is still A's (no one touched it),
  A stays as writer — no banner, no disruption.

### 11. Sleep, B takes over, then wake (W2 TakenOver via sleep)

- A opens as writer, makes unsaved edits. Sleep A.
- Wait for A's heartbeat to go stale (~3 min).
- B opens and takes over the lock while A is asleep.
- Wake A. A should detect the takeover and show the `TakenOver` banner naming B.
- Verify A is in read-only mode showing the last-saved state (unsaved edits gone).

---

## Group 6 — Edge cases

### 12. Rapid open/close

- A opens as writer, exits immediately.
- B opens the same DB right away.
- Verify no stale-lock issues — B should get write access cleanly.

### 13. Simultaneous open

- A and B both open the same DB at roughly the same moment.
- One should win the lock; the other should get reader mode with a banner naming the winner.

---

## Cross-cutting checks (verify on every test)

| Check | What to look for |
|-------|-----------------|
| **Banner specificity** | Every banner names the cause. No test should produce the old generic "You have lost write access" string. |
| **Username + machine** | Whenever another user is involved, the banner names them. If it says "another user" without a name, `LockLossNewHolder` was not populated. |
| **Read-only after demotion** | After any mid-session loss, write controls (save, edit fields) are disabled. Section list shows the last-saved state. |
| **No false demotions** | Tests 7, 8, and 10 specifically verify that transient network issues and sleep do NOT cause demotion (W1 fix). |
| **Banner before demotion** | If demotion fails or throws, the banner should still be visible (W4 fix, subsumed into W2). |

---

## Notes

- **There is no "force takeover" mechanism.** B can only take over when A's lock appears
  stale (heartbeat > 180s old). The mid-session `TakenOver` banner on a running A is
  therefore only reachable when A's heartbeat has stopped reaching the share — via network
  drop (test 9) or sleep (test 11). These two tests are the primary W2 banner validation.
- **Timing**: Heartbeat interval = 60s, stale threshold = 180s, poll interval = 60s.
  Allow up to ~4 minutes after A stops heartbeating before B sees the stale prompt.
