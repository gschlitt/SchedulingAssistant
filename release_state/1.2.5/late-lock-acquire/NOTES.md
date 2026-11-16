# Late lock acquire holds a lock nobody uses: item notes

Ledger: [RELEASE_1.2.5.md](../RELEASE_1.2.5.md). Design doc: none yet (to be written at `designs/1.2.5/late-lock-acquire.md`).

<details>
<summary>Files to read first</summary>

- `src/TermPoint/Services/CheckoutService.cs` around lines 903, 1128, 3160 and 4545: the `TryAcquire` / `ForceAcquire` call sites.
- `src/TermPoint/Services/WriteLockService.cs` around lines 469-471 (the 12 s wait) and the heartbeat start.
- `src/TermPoint/Services/NetworkFileOps.cs` around line 730: `RunAsync` gives up after 5 s and only logs it.
- `src/TermPoint.Tests/Harness/NetworkHangTests.cs`: `Hang_APutBackThatTimesOut_LandsLate_PinnedAsToday` (line 349).
- Line numbers are from the 2026-11-14 audit and may have drifted.

</details>

<details>
<summary>Decisions and why</summary>

- Greg judged this serious (2026-11-16) and asked for it to be tracked in 1.2.5.
- Not decided: the fix. Backlog idea is a per-attempt token so a given-up attempt releases whatever it created.

</details>

<details>
<summary>Dead ends</summary>

None yet.

</details>

<details>
<summary>Technical issues</summary>

- Source: first finding of the "Hang/deadlock risks audit" session (2026-11-14). No true deadlock was found; this is the one that can lock other users out.
- Mechanism: `NetworkFileOps.RunAsync` gives up after 5 s but the work keeps running. If the share recovers, the background `TryAcquire` creates the lock file, sets `IsWriter = true` and starts the heartbeat, without posting `LockStateChanged`. The user was already told the network is down.
- Result: a live, heartbeating lock this user isn't editing under; everyone else stays read-only until this process releases it or exits. A retry can also run a second `TryAcquire` on the same unprotected fields at the same time.
- Four rules: breaks the spirit of rule 2 (a lock that is ours but we don't know we hold) and rule 4 (confuses other users).
- Cheapest test: an ~8 s stall on the lock-file create in the NetworkProbe rig; look for "Acquired write lock" after "lock acquire timed out" in the log.

</details>
