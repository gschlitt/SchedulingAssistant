# TermPoint 1.2.5 backlog (moved from 1.2.4, 2026-11-11)

Greg closed 1.2.4's work list on 2026-11-11. Everything below was still open and moves to 1.2.5. Numbers are the
1.2.4 numbers: the full text, sources and any partial notes stay in `designs/spec_version_1.2.4_summary.md` (each
item there is tagged `moved to 1.2.5`) and `designs/spec_version_1.2.4.md`. Nothing here has been re-prioritised:
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
| 2.1 | Experimenting without saving, plus Save a Copy… (Greg's priority) | Decide first; two of three designs need 3.5 |
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
| 9.8 | File an Avalonia bug for the list "jump" | |

## Also open elsewhere

- `designs/slot-browser.md` (Phase 1 awaiting Greg's lock) and `designs/access-analyzer.md` (Phase 3 wireframes).
- Microsoft support case for 1.1 (Greg), and N4 (Store update link) if not checked during the 1.2.4 release.
