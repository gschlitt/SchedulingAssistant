# Write-Access Loss — Implementation Agenda

> Generated 2026-06-24 from a focused audit of the multi-user write-lock lifecycle
> (`CheckoutService`, `WriteLockService`, `MainWindow.OnWriteLockLost`, banner generation).
> Companion to `data-integrity-agenda.md` (2026-05-04). Bring this file to a fresh
> conversation as your work-order.
>
> **Driving question:** *In which scenarios can a writer lose write access, and in each,
> is the user told why?*

---

## How to use this document

1. Work findings in priority order.
2. After each fix, run `dotnet test` (test project: `src/TermPoint.Tests/`).
3. Verify against current code before trusting any line number — the solution was renamed
   from `SchedulingAssistant` to `TermPoint` (commit 2511a9a), so older docs cite stale paths.

---

## Background — how write access is lost

All three mid-session loss paths converge on a single handler chain:

```
(detector) → CheckoutService.HandleLockLossAsync()
           → WriteLockLost event
           → MainWindow.OnWriteLockLost()
           → CheckoutService.DemoteToReadOnlyAsync()   (deletes D', builds D'' from D)
```

Since the W1 fix, all three detectors now call `VerifyLockIsOursAsync` before demoting:

| # | Detector | Distinguishes "network unreachable" from "lock genuinely lost"? | Entry point |
|---|----------|----------------------------------------------------------------|-------------|
| 1 | Save-time verification | **Yes** — `VerifyLockIsOursAsync` decision table (reachability probe) | `CheckoutService.SaveAsyncCore`, step 1 |
| 2 | Wake-from-sleep check   | **Yes** — `Unreachable` → keeps writer state | `CheckoutService.OnWake` |
| 3 | Heartbeat-failure (F11) | **Yes** (after W1 fix) — matches paths 1 & 2 | `CheckoutService.OnHeartbeatFailed` |

**Key constants** (`WriteLockService.cs`): `HeartbeatIntervalSeconds = 60`,
`HeartbeatFailureThreshold = 3`, `StaleLockThresholdSeconds = 180`.

---

## Completed

---

### W1 — Heartbeat-failure path demoted without confirming the lock was actually lost

**Priority:** P0 — **✅ Done (2026-06-24)**
**Files changed:** `src/TermPoint/Services/CheckoutService.cs`, `src/TermPoint.Tests/CheckoutServiceTests.cs`
**Tests:** Group 10 (3 new tests, 803 total passing)

**Problem:** `OnHeartbeatFailed` routed straight into `HandleLockLossAsync` without calling
`VerifyLockIsOursAsync`. A 2–4 minute WiFi/VPN interruption on the writer's own machine
triggered a false-positive demotion and destroyed unsaved edits, even though no other
instance ever contended for the lock. The save-time and wake-from-sleep paths already made
this distinction; the heartbeat path did not.

**Fix:** `OnHeartbeatFailed` now calls `VerifyLockIsOursAsync` before acting:
- `NotOurs` → demotes via `HandleLockLossAsync` (genuine loss, same as before).
- `Unreachable` → keeps writer state; surfaces a transient auto-dismissing "Network
  connection interrupted" warning via `SaveFailed`. Heartbeat timer continues; a successful
  renewal clears the warning naturally.
- `Ours` → false alarm; calls `ForceRenewHeartbeat`.

A re-entrancy guard (`_heartbeatVerifyInFlight`) prevents stacking verification attempts
if `HeartbeatFailed` fires again while an earlier check is still awaiting the network.

---

## Open — P1

---

### W2 — The mid-session loss banner explains neither the cause nor the data loss

**Priority:** P1 — **✅ Done (2026-06-24)**
**Files changed:** `src/TermPoint/Services/CheckoutService.cs`, `src/TermPoint/MainWindow.axaml.cs`, `src/TermPoint.Tests/CheckoutServiceTests.cs`

**Scenario:**
Every mid-session loss — colleague takeover, externally deleted lock, clock-skew-induced
takeover — shows one string:

> "You have lost write access. You can attempt to regain it by exiting and restarting TermPoint."

The user cannot tell causes apart, and the message **omits that unsaved edits were
discarded** (D' is deleted during demotion). The original design spec had richer wording;
the current implementation regressed to a cause-agnostic line.

By contrast, the **startup/checkout** messages are specific and good
(`MainWindowViewModel.LockStatusMessage`): colleague identity + timestamp, second local
window, Controlled Folder Access with an IT-detail button, disk full, observer mode. The
gap is strictly in the *mid-session* banner.

**Note:** W1's fix already handles the "network unreachable" case — the heartbeat path now
shows a specific transient warning and does NOT demote. W2 covers the remaining demotion
causes (all of which are genuine losses where the share is confirmed reachable).

**Fix:**
Thread the cause into `WriteLockLost` (pass an enum/struct rather than a parameterless
event, or set a `LastLossCause` on `CheckoutService` before raising). Branch the banner:
- **Taken over by X** → name the holder (from `CurrentHolder`) and state that unsaved
  changes were discarded.
- **Lock file removed** → name the likely culprits (antivirus / cloud sync / backup agent)
  and suggest relocating the DB out of a synced/protected folder.

Set the sticky banner **before** awaiting `DemoteToReadOnlyAsync` so a demotion that throws
still leaves the user a message (subsumes W4).

**Effort:** ~3 hours

---

### W5 — `SourceModified` save-refusal is a dead-end with no escape

**Priority:** P1
**File:** `src/TermPoint/Services/CheckoutService.cs` (`SaveAsyncCore`, step 2, ~644).

**Scenario:**
If D's hash no longer matches `HashAtCheckout` (an external tool touched D, a cloud-sync
conflict copy replaced it, or a takeover-then-save occurred), the save aborts with a clear
message and autosave stops. But the writer still nominally holds the lock, and **every
subsequent save also fails** the same way. There is no merge, no "Save As / export a copy,"
no "force overwrite." The user is told *why* but has no way to get their work out except
discarding the session.

**Fix:**
On `SourceModified`, offer "Save a copy…" that writes D' to a user-chosen path so the work
is never trapped. (A true merge is out of scope; an escape hatch is the minimum.)

**Effort:** ~2 hours

---

## Open — P2

---

### W3 — `DemoteToReadOnlyAsync` destroys D' before confirming it can rebuild from D

**Priority:** P2 (downgraded from P0 after W1 — see rationale below)
**File:** `src/TermPoint/Services/CheckoutService.cs`
**Lines:** D' delete (~1224), snapshot build (~1240, `SetupReadOnlySnapshotAsync`).

**Scenario:**
Demotion deletes the working copy D' (which holds unsaved edits) *before* it attempts to
build the read-only snapshot D'' from D. If D is unreachable at that moment, the user ends
with no unsaved work and no usable database view.

**Why downgraded:** Before W1, the heartbeat-failure path triggered this routinely on any
transient network interruption — a common, real-world event. After W1, `DemoteToReadOnlyAsync`
is only reached when `VerifyLockIsOursAsync` returns `NotOurs`, meaning the share was
*confirmed reachable* moments earlier. The remaining risk is connectivity flickering between
the verification and the D → D'' copy — a narrow window, not a routine path. Still
architecturally wrong (deleting data before confirming the replacement succeeds), but no
longer a routine data-loss scenario.

**Fix:**
Reorder so D' is deleted only **after** `SetupReadOnlySnapshotAsync` succeeds. On snapshot
failure, retain D' and its dirty marker (crash-recovery can offer to save it at next launch).

**Effort:** ~2 hours

---

### W4 — A demotion that throws leaves no banner at all

**Priority:** P2 (subsumable into W2)
**File:** `src/TermPoint/MainWindow.axaml.cs` (`OnWriteLockLost`, ~1184–1187).

**Scenario:**
The sticky banner is set *after* `await DemoteToReadOnlyAsync(...)`. If that await throws,
control jumps to the catch (which only logs), so write controls silently disable with no
explanation. Largely subsumed by the W2 fix (set the banner before attempting demotion).

**Fix:** Set `SetSaveError(banner, autoDismiss:false)` before attempting demotion.

**Effort:** ~30 minutes (do it during the W2 pass)

---

### W6 — Clock skew and external file-touchers are unmodeled in messaging

**Priority:** P2
**File:** `src/TermPoint/Services/WriteLockService.cs` (`DetectStaleLock` ~666,
`PollLockFile` stale-age compare ~962).

**Scenario:**
Stale detection compares a *remote* heartbeat timestamp to *local* `DateTime.UtcNow`. A
reader whose clock runs minutes fast perpetually sees a healthy writer's lock as stale and
is offered takeover; if accepted, the writer is demoted. The same class of problem covers
antivirus / OneDrive / Dropbox / backup agents that delete or briefly lock the `.lock` file.

**Mitigations to consider:**
- Keep the generous 180s threshold (already done) and document a clock-sync expectation.
- When a takeover is offered, optionally compare the holder's `Acquired` time against local
  time and warn if the skew is implausibly large (likely clock drift, not a dead writer).
- Once W2 lands, the cause-aware banner already covers the external-tooling case.

**Effort:** ~2 hours (mostly the skew warning; messaging comes free from W2)

---

## Implementation status

| Finding | Priority | Status | Test file |
|---------|----------|--------|-----------|
| W1 — Heartbeat path demotes without verification | P0 | ✅ Done | `CheckoutServiceTests.cs` (Group 10, 3 tests) |
| W2 — Cause-aware, data-loss-honest banner | P1 | ✅ Done | `CheckoutServiceTests.cs` (Group 10) |
| W5 — `SourceModified` dead-end | P1 | ☐ Open | `CheckoutServiceTests.cs` |
| W3 — D' destroyed before snapshot rebuild | P2 | ☐ Open | `CheckoutServiceTests.cs` |
| W4 — Demotion throw → no banner | P2 | ✅ Done (subsumed into W2) | — |
| W6 — Clock skew / external touchers | P2 | ☐ Open | `WriteLockServiceTests.cs` |

---

## Suggested implementation order

| Step | Finding | Why |
|------|---------|-----|
| 1 | ~~W1~~ | ~~Highest leverage; removes the common false-positive demotion.~~ **Done.** |
| 2 | W2 + W4 | Honest, cause-aware messaging; W4 is a trivial reorder done in the same pass. |
| 3 | W5 | Escape hatch so externally-modified saves aren't trapped. |
| 4 | W3 | Defensive reorder; low-frequency after W1 but architecturally correct. |
| 5 | W6 | Lowest probability; messaging mostly free after W2. |

---

## Files to read at the start of a fresh session

```
src/TermPoint/Services/CheckoutService.cs      — checkout/save/demote lifecycle, VerifyLockIsOursAsync
src/TermPoint/Services/WriteLockService.cs     — lock file, heartbeat, poll, stale detection, F11 escalation
src/TermPoint/MainWindow.axaml.cs              — OnWriteLockLost handler (~1145), event wiring (~608)
src/TermPoint/ViewModels/MainWindowViewModel.cs— LockStatusMessage (~300), SetSaveError (~246)
src/TermPoint/Services/NetworkFileOps.cs       — 5s timeout wrappers + reachability probe
src/TermPoint.Tests/                           — CheckoutServiceTests.cs, WriteLockServiceTests.cs patterns
data-integrity-agenda.md                       — prior F1–F15 findings (F11 = the heartbeat-escalation feature W1 corrects)
```

**Architectural fact to keep in mind:** `App.LockService` and `App.Checkout` are static
singletons that live **outside** the DI container and survive database switches; all other
services (DatabaseContext, BackupService, repositories) are DI singletons rebuilt on every
switch. The three loss detectors all live on the static `CheckoutService`.
