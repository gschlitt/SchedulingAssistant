# TermPoint 1.2.5 backlog (moved from 1.2.4, 2026-11-11)

Greg closed 1.2.4's work list on 2026-11-11. Everything below was still open and moves to 1.2.5. Numbers are the
1.2.4 numbers: the full text, sources and any partial notes stay in `designs/1.2.4 and prior/spec_version_1.2.4_summary.md` (each
item there is tagged `moved to 1.2.5`) and `designs/1.2.4 and prior/spec_version_1.2.4.md`. Nothing here has been re-prioritised:
pick the order with Greg when 1.2.5 starts.

**Ask the `MinimumVersionToWrite` question for every item picked** (`CLAUDE.md`). Items marked *version gate* raise
it and need 1.6 (in-app Store updates) first.

## Save path and protecting unsaved work (risk review first; four rules)

| # | Item | Notes |
|---|---|---|
| 5.4 | The rename retry is too short | E2 (`designs/network-experiments-5.8.md`): a rename is refused exactly as long as a reader holds D. Size the budget from the 7.1/7.3 field reports and reader copy times; cancellation check before each attempt (rule 2) |
| 5.6 | The writer reports a server-damaged file as "changed by someone else" | Pick: a header check after the rename, or a check on a hash mismatch before choosing the wording |
| 3.5 | The recovery redesign: checks decide, Recovered Versions, no questions | Too big for 1.2.4; 2.1 waits on it |
| 3.6 | Kept work lives in roaming AppData | Belongs with 3.5 |
| 3.7 | Read-only mode never mentions changes waiting on this computer | |
| 3.8 | Changes kept on another computer are invisible | |
| 3.9 | macOS: deleting the local copy mid-session strands the edits in memory | Future; data risk |
| 3.10 | A demotion could leave our own lock file behind | Latent |
| 3.11 | A fresh assessment once the redesign is in | After 3.5 |
| 3.13 | An edit made during Restore from Backup is deleted | Data risk; retires harness finding F24. Refuse edits while a restore runs |
| 4.13 | Remainder: a lock left behind by a switch (F20) | Accepted for 1.2.4. Idea: retry the release's checked delete every ~30 s while the lock file still names this session |
| — | Switching semesters with a section card open probably throws away what was typed in it | Found 2026-11-16 by the click-and-drag entry (not hand-checked): the semester switch reloads the section list, which closes the card without saving or saying so. Refusing the switch means putting the semester picker back. Check by hand first |

## Messages (rule 4)

| # | Item |
|---|---|
| 6.1 | Error lines are invisible while an editor is open |
| 6.2 | Review the order of the banner stack |
| 6.3 | About 14 places show raw exception text |
| 6.4 | Scheduling notes typed after a lost lock vanish silently |
| 6.5 | A failed read-only copy at open shows two messages, the first one wrong |
| 6.6 | The refused-write message waits behind other notices, and repeats |
| 6.7 | "Closed unexpectedly" shown after a clean exit |
| 6.8 | The "session file missing" message doesn't say whether the work is kept |
| 6.9 | Save a Copy suggests a name that may already exist |
| 6.12 | A writer whose clock is behind looks inactive (the wall-clock hint fires on any skew over ~3 min) |
| 6.14 | Restore from Backup: two messages when the save before it fails |

## Network shares

| # | Item | Notes |
|---|---|---|
| 5.7 | Say which storage is supported (docs and Help) | Windows servers, enterprise filers and Samba are fine; synced folders and cloud gateways are not. Greg approves the wording |
| 4.11 | Cloud-synced folders: more providers, and a warning at File → Open | Shelved in 1.2.4 (the check runs when a database is created) |

## Features and user-facing gaps

| # | Item | Notes |
|---|---|---|
| 2.1 | Save a Copy… (Greg's priority) | Experimenting without saving was folded into the ledger entry "Undo and rollback before saving" (2026-11-26); Save a Copy… stays here |
| 2.4 | Hide sections that meet for only part of the semester | |
| 2.6 | Instructor names don't wrap properly in the Workload view | |
| 2.7 | Workload view: show out-of-scope instructors who teach loaded sections | |
| 2.8 | "Subjects taught" on instructors | *Version gate* |
| 2.9 | Access watches: the same all/any tag choice the grid filter got | *Version gate* |
| 2.10 | A meeting's room type should show the chosen room's type | Decide first |
| 2.11 | Copy Semester doesn't carry meeting room types | Decide first |
| 2.12 | Sections should show credits | Decide first |
| 1.6 | In-app updates for the Store build | Decide first; needed before any release that raises the version gate |

## Testing, reporting and housekeeping

| # | Item | Notes |
|---|---|---|
| 7.5 | Gaps in the exception-reporting tests | Test-only |
| 7.2 B | One numbers-only summary per run | Deferred: BugSnag can't sum metadata; needs a usage-metrics tool and a privacy decision |
| 8.10 | An automated two-machine rig | Later |
| — | Breadcrumb the results of the wake and heartbeat lock checks (Ours / Unreachable / NotOurs, and a lock re-acquired) | From a 1.2.4 production report, 2026-11-25: a wake, two failed heartbeats, then an autosave and a backup that couldn't reach the share. Bugsnag couldn't show what either check concluded, including after an earlier 6 am wake; today that goes only to the local log. Logging only, no change to the save path |
| — | Teach the harness the ~85 s death of a stalled network call | Then known finding F22 retires; test-timing builds scale the 170 s but not the 85 s (4.12 Done note) |
| — | Flaky `FocusServiceTests.Prune_LastFocusedSubjectDeleted_BecomesNone` | Failed once in a full harness run 2026-11-11, passes alone; tests don't run in parallel. Keep the `.trx` and read the message |
| — | Optional NetworkProbe runs for 4.10 (`write-bytes`, `export-write`, `rotate`) on the clumsy rig | |
| 9.1 | Campus look-ups by name and sort order silently fail | Decide first |
| 9.2 | One rule for "do two meetings share a week?" | |
| 9.3 | The shared-schedule import should use the shared day and frequency helpers | |
| 9.4 | Section import refuses semicolon- or tab-separated files | |
| 9.5 | A section picked while the section list is hidden isn't scrolled into view | |
| 9.6 | Room Browser: test that every option is valid and none is missed | |
| 9.7 | Review Avalonia 12.1.4's release notes | |
| — | Retire the section list's scroll anchoring once Avalonia fixes the grow jump | After each Avalonia release, run `tools/VspGrowDriftRepro`; when it passes, check the ScrollAnchorProbe's "anchoring off" rows show zero drift, then decide (from the closed 1.2.5 entry "Avalonia bug post") |

## From the 2026-11-12 reviews (harness review, lock-scenario review, RC2 field tests)

Sources: `designs/harness-review-2026-11.md` (R-numbers) and the lock-scenario review (local only,
`tmp/network_scenarios/lock-scenarios-review.md`; N-numbers). None loses data; all are lock or save path unless marked, so
each needs a design and risk review against the four rules.

| # | Item | Notes |
|---|---|---|
| N1 | A whole-share outage hands a live writer's lock to a polling reader | The poll skips unreachable cycles without restarting its timing, so on return the outage counts as "unchanged" and the offer comes at once with no wait. Fix: a poll that cannot reach the share clears the heartbeat watch. Pinned: `WriteLockServicePollTests.PollLockFile_WholeShareOutage_SameHeartbeatOnReturn_IsOfferedAtOnce_PinnedAsToday` |
| N3 | A sleep between the save's final check and the rename | Re-run the final check right before the rename if more than 2 s passed on either clock (the heartbeat's F19 rule). Test `I4_…AcceptedWindow` pins the window but not a long freeze |
| S38 | A "Take over as writer" offer outlives its reason (seen in M5 (5) on RC2) | Polling stops once it offers, so the offer stays after the writer wakes; clicking it re-checks (safe) and silently returns to the read-only banner with no message (rule 4). Fix idea: keep polling after an offer and withdraw it when the heartbeat moves; when a click finds the writer active, say so (wording for Greg) |
| R2b | Tell the dangerous case early | When a missing lock is put back, check D at once; if it changed, show the conflict banner then, not at the next save |
| M1A | The outage notice says "Press Save once the connection is back." but nothing tells the user when that is (seen in M1 Part A on RC2, 2026-11-13) | The notice clears silently when the heartbeat's lock check reaches the share (up to ~60 s after it returns); a Save before then just repeats the notice (safe, rule 4 only). Options: (1) wording, e.g. "This message will close when the connection is back. Then press Save."; (2) on `LocationReachableAgain` with unsaved changes, swap in "The connection is back. Press Save to save your changes."; (3) save by itself on return (save path: risk review). Wording for Greg |
| R2 rest | `FileRemovedOrChanged` names "another program" as the cause | Messages only; neutral wording for Greg |
| R3–R8 | Harness: "takes over and closes" pause actions, must-cover story list with expected outcomes, situation counters, message-cause invariant I10, cause-guess audit, design rule | Test and review work |
| — | Sideload builds: a `-Revision` option (e.g. 1.2.4.1) | Avoids same-version uninstall/reinstall on the rig |

## Also open elsewhere

- `designs/1.2.5/slot-browser.md` (Phase 1 awaiting Greg's lock). The Access Analyzer is now a ledger entry.
- Microsoft support case for 1.1 (Greg), and N4 (Store update link) if not checked during the 1.2.4 release.
