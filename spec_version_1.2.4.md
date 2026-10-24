# TermPoint 1.2.4: Candidate List

**Status:** a running list, not yet a spec. Started 2026-10-21, when 1.2.3 was cut down to ship within days.
Every entry was deferred from 1.2.3 or found while finishing it. When 1.2.4 planning starts, each one gets the
usual treatment (problem, file:line refs, fix direction, production and data notes, verification) and an order.

**Sources of truth:**
- `spec_version_1.2.3.md`: items 1–23 and the parking lot (P1–P10).
- `designs/data-persistence-review-2026-10.md`: findings F1–F16 (section 3), the redesign (section 8) and the
  harness (section 9).
- This file only points into those documents, with a one-line summary per entry.

**Already fixed in 1.2.3 (for reference):** F1, F4, F8 and F12 (design note section 3), plus spec items 1–5, 8, 9,
12, 16–19, 21–23 and 10a.

## User priorities
| Ref | What | Where it stands |
|---|---|---|
| Item 20 | **Load by subject / focus:** work on chosen subjects only, at dean's-office scale | Design approved 2026-10-20 (approach B, filter-based focus, 8 steps): `designs/load-by-subject.md` |
| — | **Section Import by User:** self-serve onboarding from an .xlsx template | Design phases 1–3 locked (c1ba192); resume at phase 4 (Data Design): `designs/section-import-by-user.md` |

## Data loss and recovery (design note section 3)
| Ref | Issue | Notes |
|---|---|---|
| F3 / item 14 | Unapplied changes in an open editor are dropped on close or database switch | The likeliest remaining loss. Small version: "Apply / Discard / Cancel" at close and switch |
| F2 / item 15 | An edit made while a save runs misses that save; a crash or the exit save then deletes it | Edit counter. The harness reproduces it (3 events) |
| F16 | A lost-lock demotion still running during a database switch leaves the new session read-only while it holds the lock | Tie a demotion to its session; refuse a switch until a running demotion ends. `CheckoutServiceTests.F16_...` is skipped until fixed |
| F5, F6, F7 | Needless "Can't Restore" after any no-edit save; restore promises that can't be kept; "Restore?" asked before checking | The section 8.9 redesign, steps 1–4 (Recovered Versions; the open decides by itself) |
| F9 | Kept work lives in roaming AppData | Move to `%LocalAppData%`, with a migration that still finds 1.2.2/1.2.3 kept files |
| F10 | Read-only (observer) mode never mentions unsaved changes waiting | Message |
| F13 | A damaged lock file is reported as "removed", in jargon | Message |
| F14 | Kept changes on another computer are invisible from this one | Probably a message only |
| F15 | Outage messages: the banner clears itself while saves still fail, the advice is wrong, and the heartbeat adds a second "safe locally" message | One persistent "can't reach the database" state |
| P2 | A demotion can orphan its own lock file | Latent, unreachable today |
| P10 | macOS: if the local file is deleted mid-session, edits survive only in memory | Future |
| 9.4 | The new assessment: section 2 of the design note redone against the redesign, then section 4's inventory | After the redesign (design note 9.5) |

## Follow-ups from the 1.2.3 fixes
| From | What | Notes |
|---|---|---|
| F4 | `PRAGMA query_only=ON` on read-only connections, as a second guard behind the `MarkDirty` refusal | Must come after the open's schema init, migrate and seed. `DatabaseContext.ConfigureConnection` runs the WAL pragma |
| F4 | Scheduling notes typed after a lost lock are dropped silently (`SchedulingNotesViewModel` returns early) | Needs a message |
| F4 hand test | **Inline error lines are invisible while an editor is open** in the management views. The line sits in the list panel, which is hidden while editing (e.g. `InstructorListView.axaml:150`). So *any* save failure in an editor shows nothing | Move the error line out of the hidden panel; about 10 views, AXAML. 1.2.3 routes the write-access message to the banner instead |
| F4 hand test | Banner order: general notifications sit below the trial and save banners (`MainView.axaml` rows 1–3) | Review the banner stack as a whole |
| F4 survey | About 14 error sites show raw `ex.Message`, which can be SQLite text (Commitments, Release, SchedulingNotes, the imports, …) | Plain wording (rule 4) |
| F4 survey | `IsReadOnlyMode` is sticky (cleared only on a failed open). `RefreshData` now uses `Mode`; audit the remaining uses | |
| F12 | `<db>.tmp` is one name for every writer | Consider per-session temporary names |
| 9.3c-2b | The regular heartbeat tick never discovers a takeover; only a save or the wake check does | Consider |
| 9.3c-2b | `SavePrompt.SessionFileMissingMessage` doesn't say whether the unsaved work is kept (rule 4) | Message |
| Harness | Flaky proof test `PauseProofTests.F16_ADemotionHeldWhileTheUserSwitches_FiresBothClausesOfI6` (about 2–7%; also at e7185f2) | The wake-check settle waits 5 × 8 ms of real time |
| Harness | Six `TheLockFileIsDeleted` cases during a held Close dropped out of the wide pause plan; cause not investigated | |
| Not pursued | A switch overlapping a close can delete kept work (harness-only reach) | User decision 2026-10-21; recorded so it isn't rediscovered as new |

## Other items
| Ref | What | Notes |
|---|---|---|
| Item 6 | Strip paths and usernames from BugSnag reports | Plan approved 2026-10-21 (6a–6c); go in 1.2.3 only if a day is spare |
| Item 7 | Report handled save/lock failures and unclean exits to BugSnag | After item 6. Today none of the remaining findings reaches us from the field |
| Item 11 | Reader Refresh: report failures honestly, recover the connection | Plan approved 2026-10-14 |
| Item 13 | Detect databases in cloud-synced folders (OneDrive, Google Drive, Box, macOS CloudStorage) | |
| Item 4 (open) | Header-only check on D; possible 4f (working copy locked vs damaged) | |
| P3 | Copy Semester doesn't carry meeting room types | Design question |
| — | **Meeting room type: show the room's type when a room is chosen** (found 2026-10-24). Today a meeting's room type is the type it *needs*: it starts at "(none)", picking a room never fills it in, and a type given to a room later never reaches its meetings (`SectionMeetingViewModel.cs:290-345`). It looks odd to users. Fix direction: when a meeting has a room, the type box shows that room's type, read directly from the room. The stored value matters only for a meeting with no room, which is the only case the Room Browser uses it (`RoomAvailabilityService.cs:250-268`; a chosen room locks the meeting). Nothing to propagate and no bulk rewrite of sections | Decide: setting the type box narrows the room dropdown, so after picking a lab only labs are listed. Accept that, or show the room's type as a separate read-only label. If the editor starts *storing* the type when a room is picked, ask the `MinimumVersionToWrite` question (likely No: 1.2.2 already keeps the field). Makes P3 matter less for meetings that have a room. The section import (`designs/section-import-by-user.md`) leaves room type blank and is right either way |
| P4 | File an Avalonia bug report for the VirtualizingStackPanel item-growth jump | |
| P6 | A section picked while the section list is hidden isn't scrolled into view when the list is shown | |
| P7 | In-app updates for the Microsoft Store build | |
| P8 | Campus look-ups by name and sort order silently fail (JSON key case) | Existing bug |
| P9 | Section-type colours (client request) | Needs a `MinimumVersionToWrite` raise per the policy |
| Avalonia | 12.1.4 was published 2026-10-09; review its notes (#22394 touches the section card's property-line icons) | |
