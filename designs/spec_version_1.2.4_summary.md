# TermPoint 1.2.4: what's on the list

A summary of `designs/spec_version_1.2.4.md` as of 2026-11-01. That file stays the record (history, evidence, file and
line references). This one only says what is to be done. It also picks up the 1.2.4 items that live only in the
field-test doc (`docs/testing/network-field-tests-1.2.3.md`, "Open issues"), in `HANDOFF.md` and in other design docs.

**How to read it.** Three levels:
1. **At a glance**: the whole release on one screen.
2. **The groups**: one line per item.
3. **Click an item** to open a short description: the problem, the fix direction, what makes it risky, and where the
   full write-up is.

**Tags on items:**
<code>data risk</code> work can be lost or stranded today ·
<code>save path</code> changes save, lock or exit code, so a risk review (Alice-and-Bob story, the four rules) comes
before the design ·
<code>version gate</code> probably raises `MinimumVersionToWrite` ·
<code>decide</code> needs Greg's call before work starts ·
<code>AXAML</code> view edits (close the tab in VS first)

---

## At a glance

About 80 items in nine groups. Most were deferred from 1.2.3 or found while testing it.

| # | Group | What it covers | Items |
|---|---|---|---|
| 1 | [Store release](#1.%20Store%20release) | The 1.2.3 update that wouldn't open; a smaller package; test flights | 6 |
| 2 | [Features and user-facing gaps](#2.%20Features%20and%20user-facing%20gaps) | Requests from Greg and clients; the licence-expiry warning | 14 |
| 3 | [Protecting unsaved work](#3.%20Protecting%20unsaved%20work) | The remaining ways edits can be lost or stranded; the recovery redesign | 12 |
| 4 | [Lock, save, open and restore](#4.%20Lock,%20save,%20open%20and%20restore) | Correctness gaps in the persistence paths | 14 |
| 5 | [Network shares](#5.%20Network%20shares) | What SMB caching, antivirus and unusual storage do to us | 8 |
| 6 | [Messages](#6.%20Messages) | Wrong, doubled, hidden or jargon messages | 13 |
| 7 | [Field reporting](#7.%20Field%20reporting) | Learning from BugSnag whether a failure ended well | 5 |
| 8 | [Testing](#8.%20Testing) | Harness gaps that let field bugs through; cheaper hand tests | 12 |
| 9 | [Smaller fixes and housekeeping](#9.%20Smaller%20fixes%20and%20housekeeping) | Bugs, consistency, upstream | 8 |

**Called out as urgent or high-value in the notes**
- **Store package:** leave out one shared Windows file, and use test flights. The 1.2.3 update wouldn't open until
  the computer restarted.
- **Unapplied editor changes are dropped at close or database switch.** This is the likeliest remaining data loss.
- **Experimenting without saving, and a real Save a Copy command.** Greg's own top request.
- **Why saves fail in the field.** BugSnag reports keep arriving that can't tell a slow server from an outage. A
  cheap timing breadcrumb would tell them apart.
- **Licence-expiry warning.** Licensed departments lose editing with no notice.
- **The GitHub Tests workflow fails on `master`.** Not investigated yet.

**Things that shape the plan**
- **The version gate.** "Subjects taught" and "any"-tag access watches each save something older versions would
  drop (colour by subject, which replaced section-type colours, lives in AppSettings and is not gated). If even one ships, 1.2.4 raises `MinimumVersionToWrite`, and in-app Store updates were
  meant to land before the first release that does that.
- **Risk reviews first.** These change the save path: skipping no-edit saves, the edit counter, per-session
  temporary files, longer rename retries, a check after the rename, the save-recovery report, test timings, and the
  harness's "hang" mode if it goes the clock route. Greg's rule is that risky save-code changes go early in a
  release.
- **Two network experiments come first.** Each one settles how a network fix is built.
- **Order constraints.** Two of the three experimenting designs need Recovered Versions. The redesign's messages
  replace 1.2.3's interim wording. The automated two-machine rig comes after the harness gaps and test timings.

---

## 1. Store release

> [!note]- **1.1 The 1.2.3 Store update wouldn't open until a restart** (a Windows bug): open a support case
> After the Store installed or updated 1.2.3, clicking TermPoint did nothing until the computer restarted. It happened
> on both of Greg's computers. TermPoint never started; instead, each click made Windows re-register the package.
> **Cause:** removing 1.2.2 couldn't delete `Microsoft.DiaSymReader.Native.amd64.dll`. Windows shares that file between
> packages, and another app (Phone Link) had it loaded. The failed removal left a stale, empty package record in
> Windows' cache, and only a restart clears it. **To do:** open a Microsoft support case with the evidence (a Process
> Monitor export on GregDeskTop), and watch for "TermPoint won't open" reports; the answer is to restart. The packaging
> hasn't changed since 1.2.2, so 1.2.2 may have done the same.
> *Source: spec, "Release and distribution"; HANDOFF next steps.*

> [!note]- **1.2 Leave that shared debugging DLL out of the package**
> TermPoint doesn't need the file: it reads Windows debug-symbol files, which TermPoint never uses. Exclude it from the
> publish output or from `build-msix.ps1`. This helps from 1.2.4 on. The 1.2.3 → 1.2.4 update can still hit the bug
> once, because the 1.2.3 package contains the file. Check that crash and log stack traces still work. Other shared
> .NET files could in principle cause the same problem.

> [!note]- **1.3 Test flights before every Store release**
> In Partner Center, release each submission first to a flight group that holds only our own accounts. Update a
> computer that has the previous Store version, click TermPoint, and only then release to everyone. This would have
> caught 1.1. A flight is also the place to test the Store-only paths (the "Store" install channel, the update link).

> [!note]- **1.4 Remove 105 MB of graphics-library debug files from the Store package**
> The Store build carries `libSkiaSharp.pdb` (84 MB) and `libHarfBuzzSharp.pdb` (21 MB). Store mode drops only
> TermPoint's own PDB today. Excluding these two makes the download noticeably smaller.

> [!note]- **1.5 The GitHub Tests workflow fails on `master`**
> It has failed since at least 1.2.2. The local suite and the harness pass. Not investigated.

> [!note]- **1.6 In-app updates for the Store build** `decide`
> Use the Store API to check for updates while TermPoint runs, show "An update is available — Update now", and honour
> Partner Center's mandatory flag (TermPoint ignores it today). This needs a Windows-specific target framework for the
> MSIX build, with changes to packaging. The payoff is a much shorter window in which old and new versions share a
> database. It was meant to land before the first release that raises the version gate, which 1.2.4 may be.
> *Source: 1.2.3 spec, parking lot (P7).*

---

## 2. Features and user-facing gaps

> [!note]- **2.1 Experimenting without saving, plus a Save a Copy… command** (Greg's priority) `decide`
> **Problem:** a user can't try something out ("what if this section moved to Tuesday?") because autosave and the exit
> save always save. There is also no general Save As: "Save a Copy…" exists only inside the save-conflict and
> can't-restore flows.
> **Possible designs:**
> - **A.** "Close without saving" at exit, offered when autosave is off.
> - **B.** A "Try things out" mode: autosave pauses, a banner says so, and the user chooses Keep or Throw away at the
>   end.
> - **C.** A Save a Copy… command in the Files menu, then work on the copy.
>
> A and B must move the work to Recovered Versions rather than delete it (rule 3), so they wait for the recovery
> redesign (3.5). They also share the close sequence with 3.1: one close should ask at most one question. C stands
> alone.
> **Pitfalls for C:** don't suggest a folder beside the shared database (colleagues could open the copy by mistake), a
> synced folder, or Documents/Desktop. "Open the most recent database at startup" would reopen the copy next time.
> Changes made in a copy can't go back into the real database. The conflict flow's export can be reused.
> *Source: 1.2.3 spec, item 26.*

> [!note]- **2.2 Licence-expiry warning**
> Today a licensed department sees only the footer line "Licensed to … (expires …)". The day after expiry, editing
> turns read-only behind a banner that can't be dismissed. The check also runs only at startup, so a session left open
> across the expiry date keeps editing until the next launch. **Fix:** within 30 days of expiry (not for permanent
> licences), show a dismissable notice: "Your license expires on &lt;date&gt;. Purchase or renew", linking to
> termpoint.ca/license. The wording should read well at 1 day and at 0 days. Test the 30/31-day boundary and a
> permanent licence. No data risk.
> *Source: spec, "Licensing".*

> [!note]- **2.3 Colour by subject** (replaces section-type colours, client request) `spec`
> Give each section type a colour, and colour sections by type in the Section List and the Schedule view. The
> presentation in each view is still to be designed. Storage is one new field on the section-type record. Set brushes
> directly on each card, never through per-type style classes (performance rule).
> *Source: 1.2.3 spec, parking lot (P9).*
>
> **Update 2026-11-03:** Greg's request is now **colour by subject**, stored in AppSettings (no version gate). Spec:
> `designs/subject-colours.md` (spec locked 2026-11-04; five implementation tasks, next task 1). **Replaces** section-type colours (decided 2026-11-03), so this item no longer needs the version gate.
>
> **Done 2026-11-04 (tasks 1–4 and Revision 1; `3b5d71e`, `d257914`, `d04cb0b`, `c33861f`):** colours per subject
> abbreviation in AppSettings, set in the Courses flyout (swatch + chooser, Suggest colours, Clear, display mode Always /
> Only when more than one subject is shown / Never). Revision 1 (Greg, after seeing tiles): a small outlined rounded
> square before the section code, not a background chip, on Schedule view tiles (0.9 × tile font) and section cards
> (11 px). New mid-tone palette; the too-dark note is gone. `MinimumVersionToWrite`: No (nothing reaches the database).
> Task 5 checks: demo registers the same services (code read) and compiles for the browser; palette spacing passes.
> **Left:** Greg's by-eye pass on real data (the list in the spec's task 4 and Revision 1).
> **Palette kept as is (Greg, 2026-11-04):** palette blue #2F6FC4 and green #3A9A3A nearly match the blue and green
> flags (OKLab 0.027 and 0.037), but marker and flag differ in shape and place; the suggester's minimum distance stays
> 0.04 (closest palette pair 0.092).

> [!note]- **2.4 Hide sections that meet for only part of the semester**
> Sections that meet for, say, the first three weeks clutter the grid. Wanted: a way to filter them out, or to suppress
> chosen sections from the grid. Not designed yet.

> [!note]- **2.5 Shorten long instructor lists on Schedule view tiles** `spec`
> **Problem:** long instructor lists make the Schedule view very wide. Greg's screenshot (2026-11-04) shows Monday alone
> filling the screen. Within a group of overlapping tiles, each side-by-side column is as wide as its widest entry
> (`ScheduleGridView.axaml.cs`, Phase 1.5). So one entry like "MECH221 T1B GH JC2 LD FM2 AD2" widens the whole column
> for the whole group, not just its own tile.
>
> **Design (option 1, chosen 2026-11-04):** show the first instructors' initials plus a count of the rest, e.g.
> "MECH221 T1B GH +4". A count, not "…", because it says how many are hidden. Hovering a shortened entry shows the
> full instructor names in the same tooltip style meeting tiles use. Entries that aren't shortened get no tooltip
> (section tiles have none today).
> - **Reuse:** meeting tiles already do this (first 5 initials + "+N", names on hover) in
>   `ScheduleGridViewModel.cs` (meeting block build). Move it into one shared helper with the limit as a parameter, and
>   use it for both. Meetings keep their limit of 5.
> - **Width measurement** must measure the shortened label, so the column actually narrows.
> - **Cost:** a tooltip is a single property set on the entry row that's already drawn; Avalonia builds the popup only on
>   hover. Set it directly, not through a style class (performance rule).
> - **Section List cards are unchanged:** they wrap names onto more lines and are a separate concern.
> - **No data risk:** display only; nothing reaches the database. `MinimumVersionToWrite`: No.
>
> **Decided (Greg, 2026-11-04):**
> - **D1.** There is no lead instructor; the first shown is simply the first in the section's stored order.
> - **D2.** One instructor's initials before "+N": "GH +4", and two instructors become "PO +1".
>
> **Done 2026-11-04:** shared helper `ScheduleGridViewModel.BuildInstructorText` (limits
> `SectionInitialsShown` = 1, `MeetingInitialsShown` = 5) used by section and meeting tiles; full names ride on
> `SectionMeetingBlock`/`TileEntry.InstructorNames` only when shortened, and the view sets a per-line tooltip from
> them. Column widths measure the shortened text. Tests in `GridPipelineTests`; full suite green apart from the
> `SubjectPaletteTests` repo-root lookup, which fails only when built to a temp output folder.
> Greg checked it by eye in the app on 2026-11-04 (Monday width, hover on a shortened line, meetings still showing 5).
>
> **Later, only if Monday is still too wide after option 1:**
> - **Option 2:** in a tall single-entry tile whose time-based height already fits a second line, put the initials on
>   line 2 at no vertical cost. Bends the one-line rule only where the space is already empty. Medium complexity.
> - **Option 3:** draw initials one font step smaller than the course and section code (an app.axaml resource). Cheap;
>   about 10–15% narrower. Could ship alongside option 1.
>
> **Rejected:** vertical text (needs a rotation wrapper per entry, doesn't work with stacked entries, hard to read);
> dropping the subject prefix when the colour marker shows the subject (colours aren't unique enough, and screenshots or
> printouts would lose the subject); re-packing the side-by-side columns (what's left of the width comes from genuinely
> different time spans).

> [!note]- **2.6 Instructor names don't wrap properly in the Workload view**
> Not investigated.

> [!note]- **2.7 Workload view: show out-of-scope instructors who teach loaded sections**
> With only some subjects loaded, an instructor outside that scope who teaches a loaded section should still appear in
> the Workload view. Not investigated.

> [!note]- **2.8 "Subjects taught" on instructors** (follow-up to Load by subject) `version gate`
> Add an explicit "Subjects taught" field on the instructor for Load by subject's focus. The derived rule stays as the
> fallback for instructors with nothing set. It was held back from 1.2.3 because a 1.2.2 client would wipe the field.
> *Source: `designs/load-by-subject.md`, "Later release (1.2.4)".*

> [!note]- **2.9 Access watches: the same all/any tag choice the grid filter got** `version gate`
> 1.2.3 gave the grid's Tags filter a "match all / match any" choice; program watches should get the same. Watches are
> saved in the database, and older versions would read an "any" watch as "all", so the version-gate answer is probably
> Yes. The two rules live today in `ProgramConflictService` and `FilterSnapshot`.
> *Source: 1.2.3 spec, item 31.*

> [!note]- **2.10 A meeting's room type should show the chosen room's type** `decide`
> Today a meeting's room type is the type it *needs*. It starts at "(none)", picking a room never fills it in, and a
> type given to a room later never reaches its meetings. Users find this odd. **Fix direction:** when a meeting has a
> room, show that room's type, read directly from the room. The stored value matters only for meetings with no room,
> and those are the only ones the Room Browser uses it for. **Decide:** setting the type box narrows the room dropdown
> (after picking a lab, only labs are listed). Either accept that, or show the room's type as a separate read-only
> label. If the editor starts *storing* the type, ask the version-gate question (probably No).

> [!note]- **2.11 Copy Semester doesn't carry meeting room types** `decide`
> Meetings are copied without their room type, even when meeting times and room assignments are included, so a
> "Remote" meeting arrives as an ordinary one. Nothing is lost, because the source semester is untouched. **Decide:**
> should room type follow the "meeting times" option or the "room assignments" option? Once 2.10 is in, this matters
> less for meetings that have a room.

> [!note]- **2.12 Sections should show credits** `decide`
> Sections should show the credits available. Should really attach credit to a course, then propagate to sections, with an over-ride in section editor.

> [!note]- **2.13 Collapse days in the Schedule view** (Greg's idea, 2026-11-04) `AXAML`
> **Now specced in `designs/collapse-days.md`** (spec locked 2026-11-05; next: task 1, tile padding). That file supersedes the design direction
> below: folded days show as strips with a count, the full week on every launch, any set of days, export says what it
> shows, padding change first.
>
> **Problem:** a busy week doesn't fit on one monitor. In real UBC data (Winter Term 1), 400-level CIVL + MECH needs
> 2,642 px and 300-level CIVL + ELEC + MECH needs 2,961 px; a monitor's grid area is about 1,500 px. With time running
> down the page and readable labels, a day holds about two side-by-side tiles, and real slices need three or more at
> busy times. Ways of narrowing the tiles themselves were tried and set aside (see "Decided against").
>
> **Idea:** let the user collapse days that can't affect the question. Sections that meet more than once keep to MWF or
> TR (163 of 199 in the same data; another 310 meet on one day), so someone placing an MWF section collapses T and R.
> Two days fit 1,500 px for both slices above (about 1,210–1,260 px). MWF needs about 1,700–1,800 px; the padding
> change below brings it close.
>
> **Design direction:**
> - M T W R F toggles in the grid toolbar, with MWF, TR and All shortcuts. Stored in AppSettings: display only, no
>   data risk, no version gate.
> - A collapsed day stays as a narrow strip with its letter, and clicking it reopens the day, so the view never looks
>   complete when it isn't.
> - The list of days shown is built in one place (`ScheduleGridViewModel`, where it picks the day numbers after the
>   grid's start and end rows).
> - **Trap:** the view works out a tile's day from its column position (`clickDay = (d / semCount) + 1` in
>   `ScheduleGridView.axaml.cs`), which assumes Monday is first and no day is skipped. With a day hidden, clicks, the
>   right-click menu and program-conflict marks would point at the wrong day. Each column must carry its real day
>   number; check the Room Browser's slot tiles and the conflict lines for the same assumption.
> - **Less padding (do with this item):** each tile's width allowance (28 px) assumes 3 px multi-semester borders and
>   includes an 8 px safety margin. With one semester shown, about 10 px per side-by-side column could come back,
>   roughly 100–150 px on a busy week.
> - **Open:** should printing and export follow the collapsed days? Does Saturday (when turned on) get a toggle too?
> - **Later:** the Slot Browser could collapse the grid to the days of the pattern it's searching.
>
> *Mocks built from the real data (local only, in the ignored `tmp/` folder on GregDesktop):*
> `tmp/monday_tile_layouts.html`, `tmp/week_fit_mock.html`, `tmp/week_more_mock.html`.

> [!note]- **2.14 Show the chosen subjects in the Schedule View's title** (Greg, 2026-11-04) `AXAML`
> When subjects are chosen (the subject focus from Load by subject), only the app's top menu bar says so, where it's
> easy to miss. With the Schedule View detached into its own window it can't be seen at all, so the grid looks like
> the whole timetable when it isn't. **Fix:** show the chosen subjects in the Schedule View's title, each with its
> colour marker when colour by subject is on (for example "Schedule View · ■ CIVL ■ MECH"). The title is a fixed
> "Schedule View" today (the panel header in `MainView.axaml`); the detached window needs the same text. Keep it short
> when many subjects are chosen (the first few, then "+N"). Display only: no data risk, no version gate.

>[!note]- **2.15 Mute instructor conflicts, mute room conflicts
---

## 3. Protecting unsaved work

Everything in this group is checked against rule 3 (never delete the only copy of someone's unsaved work).

> [!note]- **3.1 Unapplied editor changes are dropped at close or database switch** `data risk` `decide`
> **Problem:** edits in an open inline editor exist only in memory until Apply. Closing the window or switching
> database throws them away silently. The same happens when TermPoint goes read-only after losing the lock: the reload
> closes the section editor and drops its change without a word. Nothing could have been saved by then, but the user
> isn't told. The persistence review rates this the likeliest remaining loss.
> **Fix direction:** before a close or a switch, detect an editor with unapplied changes and ask Apply / Discard /
> Cancel. Apply runs the editor's normal validation, and the window stays open if validation fails. This needs a
> common "has unapplied changes" contract across the editors, so inventory them first.
> **Decide:** always ask, or apply silently when the changes are valid? Fit this into one close sequence with 2.1.
> Surviving a hang or crash is out of scope (that would need saved drafts).
> *Source: 1.2.3 spec, item 14; field-test open issue A3.*

> [!note]- **3.2 An edit made during a save can be lost** `data risk` `save path`
> An edit made while a save is running isn't covered by that save. The save clears the "unsaved" marker anyway, so if
> the computer crashes before the next save, or the exit save finishes, the edit is deleted. The harness reproduces
> this in three steps. 1.2.3 added the exit guard (repeated X, "Saving and closing…") but not the fix. **Fix
> direction:** an edit counter, so that a save clears the marker only if no edit arrived while it ran.
> **Partly fixed (2 Nov 2026) by 3.12:** when the edit lands after the save's marker delete (it re-writes the marker),
> the close or switch no longer deletes it; it is offered back at the next open. The crash route remains.
> *Source: spec, data-loss table (F2 / item 15).*

> [!note]- **3.3 A demotion still running during a database switch lands on the new session** `data risk` `save path`
> Losing the lock starts a demotion that copies a read-only snapshot. If Bob switches database during that copy, the
> demotion finishes against the *new* database. The new session then holds that database's lock but is read-only as
> far as the save code knows. Edits made there may not be saved at close, and they have no marker. A unit test (skipped
> until fixed) and the harness both reproduce it. **Fix direction:** tie each demotion to the session it started for,
> and check that before every state change. Refuse a switch while a demotion is running.
> *Source: spec, data-loss table (F16).*

> [!note]- **3.4 A session with no edits still rewrites the shared database** `data risk` `save path`
> **Problem:** when the shared database was last written by something other than TermPoint's save (a new database, a
> restored backup, 1.2.2 opening it directly, another SQLite version), a session's first save rewrites it even with no
> edits, because five header fields differ. Anyone's changes kept from before then can't be restored. There is a
> second route: after "Can't Restore" falls back to a normal open, the "save the restored changes at once" step still
> runs, saving a session that restored nothing.
> **Fix direction:** skip the upload when the save's snapshot matches the shared database with those header fields
> ignored, plus a guard for the case with no marker. Run the post-restore save only when the open really took on the
> kept changes. This was designed, probed and risk-reviewed for 1.2.3, then moved out as a risky save-code change.
> *Source: 1.2.3 spec, section 29; field-test open issue A9.*

> [!note]- **3.5 The recovery redesign: checks decide, Recovered Versions, no questions** `save path`
> **Problem:** kept changes (left by a crash, a lost lock or a failed exit save) sit in the one place a normal open
> needs. So the user has to be asked about them first, some answers (or the X) delete them, and "Restore?" is asked
> before anything checks whether a restore is still possible. 1.2.3 ships interim honest wording.
> **The design (agreed 2026-10-18):** checks decide, not the user. At the next open:
> - **Database unchanged:** add the changes and save.
> - **Database changed:** move the changes into a TermPoint-managed Recovered Versions folder (a checked copy first,
>   then delete the original), then open normally.
> - **Can't tell:** stop; nothing moves.
>
> A save refused because someone else changed the database shows a banner with a Reopen button. Closing or switching
> moves the work without asking. A Recovered Versions list offers Open (read-only), Save a Copy… and Delete. The new
> messages say plainly that recovered work is "for reference".
> **Four steps:** the store (no UI); the open decides by itself; the conflict banner; the list and the new messages.
> Each step removes the code it replaces.
> *Source: `designs/data-persistence-review-2026-10.md`, section 8.*

> [!note]- **3.6 Kept work lives in roaming AppData**
> Roaming AppData can roam back an older copy, or sit on a server under folder redirection. Move kept work to local
> AppData, with a migration that still finds files kept by 1.2.2 and 1.2.3. Recovered Versions is meant to live there
> too.

> [!note]- **3.7 Read-only mode never mentions changes waiting on this computer**
> A user who opens read-only isn't told that unsaved changes from earlier are waiting on this computer. Add a message;
> the redesign's draft wording covers it.

> [!note]- **3.8 Changes kept on another computer are invisible**
> Changes kept on Bob's laptop can't be seen when he opens the database from his desktop. Hard to fix; probably a
> message only.

> [!note]- **3.9 macOS: deleting the local copy mid-session strands the edits in memory** `data risk` (future)
> On macOS an open file can be deleted. If the local copy is deleted during a session, saves fail safely, but the edits
> exist only in the deleted file that the connection still holds, so they are lost at close. Autosave meanwhile keeps
> promising a retry. Possible design: snapshot through the live connection into a recovery file, and tell the user.

> [!note]- **3.10 A demotion could leave our own lock file behind** (latent)
> Unreachable today, because a demotion only ever follows a lost lock. If a future change demotes while still holding
> the lock, the background delete of the lock file and reader mode's read of it can collide, leaving our own lock file
> on the share. If that happens: open the read with delete sharing, or wait for the release to finish first.

> [!note]- **3.11 A fresh assessment once the redesign is in**
> Redo the persistence review's situation-by-situation walkthrough against the new behaviour, then its inventory of
> the other ways data can be lost (backups, imports, Copy and Empty Semester).

> [!note]- **3.12 A failed demotion, then Close, deletes the unsaved edit** `save path` **data loss**
> Alice has an unsaved edit when her computer wakes to find Bob took over. The switch to read-only keeps her edit, but its
> copy of the database for reading fails (another program has it open, or the network drops at that moment). She stays
> on her own working file, read-only. At Close the skipped final save reports Success, so the teardown deletes the file
> despite its unsaved-changes marker. No message. **Fix:** the working-copy delete itself refuses a file that still has
> unsaved changes (rule 3 at the data layer); the end of a restore deletes it through the discard path.
> **Done (2 Nov 2026):** with a regression test; it also closes the close-time route of 3.2.
> *Source: harness, another program holding a file (8.3), known finding F21.*

---

## 4. Lock, save, open and restore

> [!note]- **4.1 Every writer uses the same temporary file name** `save path`
> **Seen in the field:** a writer frozen mid-save (a Surface in Modern Standby can do this) held the temporary upload
> file open. For 6 minutes the new writer's saves all failed, with a message blaming "another program". Nothing was
> lost.
> **Worse case, found in 1.2.3:** Bob's laptop sleeps during his upload, and Alice takes over and starts her own upload
> under the same name. When Bob wakes, his failed upload deletes the temporary file, which may be Alice's. That is a
> write to the share without the lock.
> **Fix direction:** give each session its own temporary name. That removes every such delete at once.
> *Source: spec, follow-ups table (F12); field-test open issue A11; 1.2.3 spec item 32, "Not fixed now".*

> [!note]- **4.2 An exit save that loses the lock starts a demotion while the window closes** `save path`
> When the exit save is refused because the lock was lost, the in-place demotion still starts, and its last steps can
> run after services are disposed. The log gets an ERROR ("Cannot access a disposed object"), BugSnag probably gets a
> report, and in a rare ordering "Changes Not Saved" isn't shown. No data risk: the local copy and its marker are kept,
> and Restore is offered next time. Seen in the field. **Fix direction:** skip the demotion while closing, or read the
> marker state before the save.

> [!note]- **4.3 Restoring a backup leaves the old process running; the relaunch should reopen the restored database**
> After a Save & Backup restore, the old TermPoint process stays alive with no windows until sign-out (about 265 MB).
> It may hold up a Store update, and it reports an unclean exit later. No data risk: the lock, guard and connection are
> released first. The database chooser has been ruled out. The suspects are the relaunch (which starts just before
> the main window closes) and the stale-session teardown. Separately, the relaunched copy shows the chooser instead of
> reopening the restored database. **Fix direction:** once the main window has closed, force a shutdown (guarded
> against "already shutting down"), and pass the restored path to the relaunched copy.
> *Source: spec, "Item 5 (M11)"; the field-test doc has the stack and heap evidence.*

> [!note]- **4.4 Read-only connections: a second, database-level guard**
> 1.2.3 refuses writes in read-only sessions in TermPoint's code. Also turn on SQLite's own read-only switch
> (`query_only`) for read-only connections. It must be applied after the open's schema setup, migration and seeding.

> [!note]- **4.5 Audit the "read-only" flag that never clears**
> `IsReadOnlyMode` is cleared only by a failed open. Refresh now goes by the session's mode instead; check the
> remaining uses.

> [!note]- **4.6 Reader Refresh: reopen the connection after a failed refresh**
> If a reader's refresh fails after the connection was closed (for example, antivirus holding the file), the
> connection is never reopened, and the session is dead until restart. The plan was approved in October: always
> reopen, on the new data or on the previous copy, retry a failed reopen, and report the outcome in the existing yellow
> row. The honest messages shipped in 1.2.3; the reconnection didn't.
> *Source: 1.2.3 spec, item 11.*

> [!note]- **4.7 Opening: a lighter check of the shared database; locked vs damaged local copy** `decide`
> Two leftovers from 1.2.3's open rework:
> - **(a)** The open-time check reads the whole shared database, which can block a writer's save while it runs. A
>   16-byte header check would do here, leaving the full check to the local copy.
> - **(b)** A local copy that another program has open (antivirus, say) is treated as damaged: the user gets "may be
>   corrupt" with a restore offer, or "Unsaved Changes Lost… damaged" in crash recovery. Tell a locked copy from a
>   damaged one.
>
> *Source: 1.2.3 spec, item 4.*

> [!note]- **4.8 A slow link stops the open** (measure, then decide)
> With about 220 ms added to each round trip (clumsy), every open stopped at the full integrity check of the shared
> database, which has a 5-second step limit. Saves at the same lag worked. Ordinary home VPNs add far less delay.
> **Next:** find the latency at which opening starts to fail, and what the user is told then. Overlaps with 4.7 (a).
> *Source: field-test open issue A6.*

> [!note]- **4.9 Backups turn over too fast** `decide`
> A backup is made at every open and five are kept, so five opens in a row delete every older backup. A user who
> restarts a few times while troubleshooting a bad edit loses the backups from before it. Consider count versus age:
> for example, always keep the newest backup from each of the last few days. The persistence review adds two more
> points: there is no "skip if unchanged", and a backup that fails its integrity check still counts toward the five.
> *Source: field-test open issue A7.*

> [!note]- **4.10 24 network file calls outside the save path have no deadline**
> `tools/NetworkProbe/GAP-AUDIT.md` lists them; each could freeze the UI on a dead share. 1.2.3 fixed the backup-cleanup
> call.

> [!note]- **4.11 Cloud-synced folders: more providers, and a warning at File → Open**
> Detection misses Google Drive for desktop, Box and macOS CloudStorage (1.2.3 added SharePoint and Teams libraries on
> Windows). File → Open and Recent don't check the folder at all, for any provider: the warning shows only in the
> wizard and in New Database. Adding it needs a message route and wording, and touches the open flow. Warn, don't
> block.
> *Source: 1.2.3 spec, item 13.*

> [!note]- **4.12 A held heartbeat puts its own lock back over the new holder's**
> The heartbeat reads its lock, writes a temp file, then moves it onto the lock file. A computer that sleeps between the
> last two steps, while someone takes over, replaces the new holder's lock with its own. No data risk (every save checks
> the database is as last seen), but the two computers fight over the lock.
> *Source: harness lock-file check (8.5), known finding F19.*
>
> **Done (3 Nov 2026, with 4.14):** two parts.
> - *A check counts for 2 seconds* (`WriteLockService.LockCheckFreshness`, not scaled). Before the heartbeat's temp
>   write and its move, the release's delete, and both takeover deletes (`ForceAcquire`, `TryReclaimDeadHolder`), a
>   check older than 2 s on either clock (steady or wall) is read again first. This closes the sleep/freeze route.
> - *Takeover confirmation 70 s → 170 s* (`StaleConfirmationSeconds` = 60 next beat + 10 slow beat + 85 Windows
>   give-up + 15 margin; stays below the 180 s stale threshold). This closes the stalled-network route for any clock
>   skew. The wait dialog now says "(up to 3 minutes)" (minutes from 90 s on).
>
> *Experiment E1 (3 Nov, GregDeskTop Win10 → Mekler, NetworkProbe `--late-landing`):* an abandoned SMB delete or
> rename lands when the network returns if the outage is ≤ 60 s; at 90 s and 180 s outages it dies about 85 s
> (85.4–85.8 s) after it was issued. Assumes the writer's SessTimeout is the Windows default (60 s); a raised
> SessTimeout lengthens the 85 s. Report: `tmp/netprobe/netprobe-20261103-093522.md` (local).
>
> F19 is off the harness's known list (proof test in `FixedFindingsProofTests`). Full suite with harness: 4182
> passed, 0 failed. MinimumVersionToWrite: **No** (nothing new is saved).
>
> *Left open:* (1) the harness doesn't model the ~85 s death of a stalled operation, so F22 stays known (hung delete
> landing after a takeover); an H-item for the harness. (2) Test-timing builds scale the 170 s but not the 85 s, so
> they are not protected against late landings. (3) Later options: record each writer's SessTimeout in the lock
> file; probe SessTimeout=30 and macOS. (4) Separate item to place: the save's dirty-marker write sits between its
> final check and the rename.

> [!note]- **4.13 A lock left behind by a switch makes you read-only when you come back**
> If another program holds the lock file (so it can't be deleted), or the database (so the switch's final save fails)
> when the user switches database, the old lock stays, naming their own live session. Switching back, they are told
> "Someone else is editing this database right now" and their kept changes can't be restored yet. No data loss.
> *Source: harness, another program holding a file (8.3), known finding F20.*

> [!note]- **4.14 A held release deletes the new holder's lock**
> A release checks the lock file is its own, then deletes it. A computer that sleeps between the two deletes the lock
> of whoever took over meanwhile, so a third person could open for editing alongside them (saves stay protected: the
> second saver is refused and keeps their changes). Same shape as 4.12; fix both together.
> *Source: harness hang mode (8.2), known finding F22.*
>
> **Done (3 Nov 2026):** fixed with 4.12, see there. The freeze route is closed by the 2 s check (new crash
> point `Release.AfterOwnershipCheck`); a stalled delete is covered by the 170 s confirmation. F22 stays on the
> harness's known list only because the harness doesn't model the 85 s limit.

---

## 5. Network shares

From the 2026-10-30 review of SMB client caching, antivirus and sync agents holding files, and renames that aren't
atomic. **Verdict:** nothing found can lose data or let two people write at once. The gaps are failed saves and wrong
messages, plus one kind of storage the design can't protect. Nothing here touches the version gate.

> [!note]- **5.1 Lock checks start with an existence test that Windows may answer from its cache**
> Windows can answer "does the lock file exist?" from its client cache: a not-found answer can be about 5 s old, file
> information about 10 s. The effects:
> - A stale "missing" makes the writer delete and re-create its own lock, then tell Bob "the lock file was removed and
>   put back" when nothing happened.
> - A reader can be told "you can edit now" early or late (harmless).
> - A failed create shows the can't-write message instead of opening read-only.
>
> No data risk. **Fix direction:** read the file directly, treat file-not-found as "missing", and keep the existing
> folder probe. That also removes a race between the existence test and the read. Run experiment 5.8 (1) first.

> [!note]- **5.2 Two code comments promise more than SMB gives**
> The check just before the rename treats an unchanged size and time as "database unchanged", but that information can
> be 10 s old. This is covered, because the lock check just before it refuses first. The 500 ms "SMB cache delay"
> before a reader's refresh does nothing. Correct the comments, and remove or re-comment the delay. No behaviour change.

> [!note]- **5.3 Our own readers block the writer's rename**
> Readers hash and copy the shared database and read the lock file without delete sharing. While any reader opens or
> refreshes, the writer's rename is refused. **Story:** Alice refreshes over VPN (a 4-second copy), Bob's autosave
> lands during it and fails with "in use", and the next autosave works. It gets worse with more readers. **Fix
> direction:** add delete sharing to our read opens. That certainly works on Samba, but a Windows server may still
> refuse; experiment 5.8 (2) decides.

> [!note]- **5.4 The rename retry is too short** `save path`
> Three attempts 150 ms apart (about 0.3 s) is short for a server-side virus scan of a freshly written multi-MB file,
> or for a sync client. **Fix direction:**
> - Growing delays (about 4.6 s in all), with their own budget, separate from the 5-second "share hung" deadline.
> - A cancellation check before each attempt, so an abandoned loop can never rename late (rule 2).
> - Leave the heartbeat's rename as it is.
>
> See also 7.1 on whether the 5-second deadline is too short.

> [!note]- **5.5 A reader's Refresh can swap in a damaged copy**
> Refresh compares hashes and swaps the copy in without an integrity check. A damaged shared database that has stopped
> changing passes, and the reader then gets SQLite errors at query time instead of a clear message. **Fix direction:**
> run the existing local-copy integrity check before the swap (milliseconds). On failure, keep the old copy and show
> the existing refresh-failure line.

> [!note]- **5.6 The writer reports a server-damaged file as "changed by someone else"** `save path`
> Nothing re-reads the shared database after the rename. If the server damaged it, the next save's hash differs and the
> user is told someone else changed the database. The work stays safe, but the message is wrong. **Fix direction (pick
> one):** a cheap header check right after the rename, or a header or integrity check on a hash mismatch before
> choosing the wording. Related to 4.7 (a).

> [!note]- **5.7 Say which storage is supported**
> Renames that aren't atomic come mainly from synced folders and multi-site cloud gateways. On those, the lock itself
> fails (each machine's lock create succeeds in its own local copy), and no checksum can fix that. A gateway behind a
> mapped drive can't be detected. Windows file servers, enterprise filers and ordinary Samba NAS boxes are fine. Say
> so in the docs and Help.

> [!note]- **5.8 Two experiments on the three-machine rig**
> 1. Does opening the lock file see a new or deleted lock sooner than an existence test does? This decides 5.1.
> 2. Does a rename over a file that another machine holds open with delete sharing succeed, on the Windows server and
>    on Samba? This decides whether 5.3 is enough there, or whether 5.4 has to carry it.

---

## 6. Messages

Everything in this group is checked against rule 4: one message per problem, in plain words, saying what happened,
whether the work is safe, and what to do next.

> [!note]- **6.1 Error lines are invisible while an editor is open** `AXAML`
> In the management views the inline error line sits in the list panel, which is hidden while an editor is open (the
> Instructors view, for example). So any save failure inside an editor shows nothing. Move the line out of the hidden
> panel, in about 10 views. As a stopgap, 1.2.3 sends the write-access message to the banner.

> [!note]- **6.2 Review the order of the banner stack** `AXAML`
> General notifications sit below the trial and save banners. Review the stack as a whole.

> [!note]- **6.3 About 14 places show raw exception text**
> These include Commitments, Release, Scheduling notes and the imports. The text can be raw SQLite output. Replace it
> with plain wording.

> [!note]- **6.4 Scheduling notes typed after a lost lock vanish silently**
> After a lost lock, the notes panel returns early without saving, so the note is dropped without a word. It needs a
> message.

> [!note]- **6.5 A failed read-only copy at open shows two messages, the first one wrong**
> When someone else holds the lock and the read-only copy fails (rare: a network drop or antivirus during a 50 ms
> copy), the user first sees "Checkout Failed — Could not open the database for editing…". Only then comes the right
> prompt, "Couldn't Open the Database… Try again / Close". **Fix:** a separate outcome for this case that goes straight
> to the right prompt. No data risk.
> *Source: spec, follow-ups table (A13).*

> [!note]- **6.6 The refused-write message waits behind other notices, and repeats**
> In the field tests, the refused-write message waited behind a licence notice that was already showing, and each
> refused Apply queued another copy of it.
> *Source: field-test open issue A4.*

> [!note]- **6.7 "Closed unexpectedly" shown after a clean exit**
> The notice "Recovered the lock from a previous session that closed unexpectedly" also appears after a clean exit
> whose final save failed (the lock is left in place on purpose then). Say "unexpectedly" only when a stale running
> file was found too.

> [!note]- **6.8 The "session file missing" message doesn't say whether the work is kept**
> Reword it so it does.

> [!note]- **6.9 Save a Copy suggests a name that may already exist**
> The can't-restore flow always suggests "&lt;name&gt; (recovered).db", even when that file exists. Suggest the next free
> name instead. The redesign (3.5) may remove this flow anyway.
> *Source: field-test open issue A10.*

> [!note]- **6.10 Closing or switching after a save problem shows a banner and a dialog**
> When the save that a close or a switch runs finds a problem (someone took over, the location can't be reached, the
> lock file was removed, the file is in use), the user sees the problem's banner and then "Changes Not Saved". Both say
> what happens to their changes. **Fix:** in that case show only the dialog. Also check that a switch never leaves the old
> database's "read-only for you now" banner showing over the new database. No data risk.
> *Source: harness message checks (8.6), known finding F17.*

> [!note]- **6.11 Two different lock-lost banners for one loss**
> Seen once in the harness: a heartbeat check still running while the user switches database shows "Another program
> removed the file…" and then "… has taken over editing". Not investigated yet; possibly related to the demotion that
> lands on the new session (F16).
> *Source: harness message checks (8.6), known finding F18.*

> [!note]- **6.12 A writer whose clock is behind looks inactive**
> Bob edits with his clock an hour behind. Alice opens and is told his lock "hasn't been active for 55 minutes". Taking
> over still waits for his heartbeat to stand still, so nothing is lost, but the message is wrong. The wall-clock hint
> in the stale check fires on any skew over about 3 minutes.
> *Source: harness clock skew (8.4).*

> [!note]- **6.13 A "changes kept" banner outlives a Discard**
> After a failed switch save, Can't Restore, and a confirmed Discard, a banner still says the changes are kept. Long
> route with another program holding the database; not investigated.
> *Source: harness clock skew walks (8.4), known finding F23.*

---

## 7. Field reporting

> [!note]- **7.1 Time each save, to tell a slow server from an outage** (cheap; do this first)
> Reports keep arriving from users' machines: a run of successful saves, one "location did not respond", then nothing.
> Two explanations fit. Either a slow server sometimes misses the 5-second deadline and the next autosave works, or a
> real outage stranded the user. **Experiment:** add the elapsed milliseconds to the save-success breadcrumb, and add
> the deadline that was hit and the last few save times to the failure report. Saves that were already taking 3–4 s
> mean a slow server; fast saves followed by a sudden failure mean an outage. It's a breadcrumb and a metadata field
> only, so it can ship first. If a slow server is confirmed, ask whether 5 s is too short; changing it is a save-path
> change, so a risk review comes first.

> [!note]- **7.2 Report when a save recovers after a failure** `save path`
> BugSnag shows that a save failed, but never that a later one worked: success is only a breadcrumb, and each failure
> kind is reported once. **Fix:** when the first save succeeds after a reported failure, send one report ("recovered
> after N attempts, S seconds"), and do the same for refresh. The code only reads state and reports, and must never
> throw or block.

> [!note]- **7.3 Report a rename blocked by another program**
> Today this goes only to the local log. Send one counted event (numbers only, through the scrubber). It can ship
> before 5.3 and 5.4, to learn whether that problem happens at all.

> [!note]- **7.4 Log the choice on "Restore Unsaved Changes?"**
> In the field tests a kept edit vanished, and the log couldn't say whether Restore or Discard had been chosen. Log one
> INFO line per choice. The redesign removes the question later.
> *Source: field-test open issue A5.*

> [!note]- **7.5 Gaps in the exception-reporting tests** (test-only)
> 1. **Most important:** nothing tests that the production BugSnag client goes through the privacy scrubber. Remove
>    that line from `App` and no test fails. Move the client setup into a testable factory.
> 2. The scrubber is tested only on example strings. Add realistic Windows and macOS messages: long paths, file URIs,
>    accented user names, names in stack frames, and paths with parentheses.
> 3. Names that tests register with the scrubber are never cleared. Add a test-only reset.
> 4. The lock-loss tests poll with a 10-second deadline. If they flake on CI, raise the deadline.
> 5. Decide whether a swallowed reporting failure should write a log line, then test it, or say why not in the test.

---

## 8. Testing

The 1.2.3 field tests took about 18 hours. Nearly every finding came from something the harness simulates more
kindly than reality. So: close the harness gaps first, then make the remaining hand tests cheaper.

> [!note]- **8.1 Harness: run the wake check and the heartbeat together**
> The harness's sleep step runs them one after the other, but a real wake (or a frozen process resuming) fires both at
> once. Both field bugs that needed new test builds came from this. Run them concurrently (consider the reader poll
> too), and harden the 40 ms quiet window when settling background work; that window caused the crash-sweep timing
> failure.

> [!note]- **8.2 Harness: a network fault that hangs instead of failing at once**
> Today "network down" answers every call with an immediate timeout, so a call that blocks before its deadline starts
> can never show up. (That is the bug the probe found.) Add a hang mode in which calls block until the network
> returns. The deadlines use real time, so either keep such walks few (5 s each) or give the deadlines the test clock.
> The test-clock route changes the save path, so a risk review comes first.
> **Done (2 Nov 2026):** the deadline reads a replaceable clock (no other change); hang mode, including at the A1 spot.
> Found 4.14.

> [!note]- **8.3 Harness: another process holding a share file open**
> Add an event that holds the database, its temporary file or the lock file open with a chosen sharing mode. Let the
> pause sweep keep a paused save's files open as well.
> **Done (2 Nov 2026):** the event, nine pause actions during held saves, and power cuts with a file held. Found 3.12
> (data loss) and 4.13. Also seen, not yet items: with the lock file held, the save banner wrongly says the location
> can't be reached; with the database held, a close with nothing to save still shows "couldn't save".

> [!note]- **8.4 Harness: one clock per computer**
> One fake clock serves every computer, so clock skew can't be expressed. Give each computer its own wall-clock offset
> over a shared steady clock.
> **Done (2 Nov 2026):** staleness holds with clocks ±1 h off, and clock jumps don't cost the lock. Found 6.12 and 6.13.

> [!note]- **8.5 Harness: a computer rewrites only its own lock file**
> The heartbeat renewing someone else's lock (fixed in 1.2.3) broke no invariant. Add one: a computer rewrites a lock
> file only if the file names its own session (creating a lock and a confirmed takeover aside). Record the no-edit save
> rewrite (3.4) as a known finding.
> **Done (2 Nov 2026):** the check is in; it found 4.12. The no-edit rewrite touches the database, not the lock file, so
> the check does not fire on it.

> [!note]- **8.6 Harness: check messages for "one per problem" and banned words**
> The harness records every dialog, but checks neither rule 4's "one message per problem" nor the banned words
> ("working copy", "local copy", "safe", paths, log text). Add both. Also add an event for the read-only copy failing
> at open, which would have caught 6.5.
> **Done (2 Nov 2026):** both checks are in. The wording check found nothing. The pair check found 6.5 and two new
> problems, 6.10 and 6.11, all recorded as known findings until fixed. The database chooser's dialogs are not checked.

> [!note]- **8.7 Test timings: one scale factor for the lock timers** `save path`
> About 50 minutes of each takeover pass is spent waiting on lock timers (heartbeat 60 s, stale 180 s, and so on). Put
> them in one timings object scaled by one factor, because the safety rests on the ratios. At ×6, the heartbeat is
> 10 s and stale is 30 s. Never scale the 5-second network deadline, or anything Windows owns.
> **Risk:** Bob points a test-timed build at a real department database. It judges Alice (on 1.2.3) stale after 30 s
> and takes over while she works. The database stays safe, but she is switched to read-only mid-edit.
> **Guards:**
> - Sideload builds only, plus an environment variable.
> - The lock file records its scale, and a computer judges staleness only for locks at its own scale.
> - "Test timings" in the title bar.
> - A test that pins the production values.
>
> **Done (2 Nov 2026), at ×3 at most:** the 5 s network deadline doesn't scale, so at ×6 one slow heartbeat (~15 s)
> would leave too little margin before stale (30 s). A lock is judged stale at the speed recorded in it, so a test build
> and a normal one never misjudge each other.

> [!note]- **8.8 Scripted setup and checks**
> PowerShell from one console through Remoting: reset the test database, launch through a scheduled task,
> kill/suspend/resume by PID, sample the lock file, compare hashes, list test sections in the database and in kept
> copies, and run integrity checks. Add a log checker that looks for each scenario's expected lines in order, with no
> ERROR, and prints pass or fail. Fold in the existing `tmp\handtest_*` scripts. No product change.
> **Built (3 Nov 2026), VM parts not yet run:** the `tools/FieldTest` module (`README.md` there). Machines are listed
> in a gitignored config: this computer, VMs over PowerShell Direct, and an optional WinRM computer. Scenario files
> check the logs for M1 B, M2 C1/C3, M4 B2/B3 and M5. The handtest scripts are folded in. The local commands pass a
> self-test in PowerShell 5.1 and 7. The VM commands are untested until a VM exists.
> Dialog-only outcomes (the restore offer, the banners) have no log line, so they're still checked by eye.

> [!note]- **8.9 A Hyper-V VM as the second and third computer**
> Suspending a VM freezes the whole machine, network included. That is closer to real sleep than suspending the
> process, it can be scripted to the second, and there are no Modern Standby self-wakes. Checkpoints reset a VM. A
> second VM is the third computer the field tests never had, and a Windows 11 VM covers the untested Windows 11
> checks. Keep one real Surface sleep per release.
> **Started (3 Nov 2026):** `tools/FieldTest/New-TestComputer.ps1` makes the VM:
> - Generation 2, with a virtual TPM and Secure Boot. Windows 11 installs even though this host's i5-7500T can't run
>   it itself.
> - Checkpoints hold memory, so a reset brings back a signed-in computer.
> - An external switch on the wired adapter.
> - A copy mode for the third computer.
>
> Next: Greg downloads the Windows 11 ISO and creates TPVM1. The first `Suspend-TestComputer` shows whether a paused
> VM's clock jumps on resume. If it doesn't, the wake check won't fire; try `-Mode Save`.

> [!note]- **8.10 Later: an automated two-machine rig**
> Extend `tools/NetworkProbe` so each machine runs the real checkout and lock services as an agent, coordinated by a
> step file on the share. The main contention, takeover and exit scenarios then become scripts that check outcomes
> directly. Do this after 8.1–8.9.

> [!note]- **8.11 Trim the hand-test tier**
> Once 8.1–8.6 are in, drop the hand tests that found nothing new in 1.2.3 (they re-checked logic the harness
> covers), keeping their on-screen wording checks in one short pass. Keep by hand only what can't be simulated:
> - real SMB behaviour;
> - the on-screen messages;
> - Avalonia and process shutdown;
> - MSIX install and upgrade;
> - a real 1.2.2 alongside;
> - one real sleep;
> - macOS;
> - Store flights.

> [!note]- **8.12 Flaky tests and small harness cleanups**
> - A pause-proof test fails about 2–7% of the time; its wake-check settle waits 5 × 8 ms of real time.
> - A fault-seam test caught a background task left over from an earlier test. Settle that test's work in `Dispose`,
>   or make the recorder ignore paths outside its own folder.
> - Six "lock file deleted during a held close" cases dropped out of the pause plan. Cause unknown.
> - From HANDOFF: after a release and a fresh checkout, the heartbeat-failure handler is subscribed twice (only the
>   wake handler is unsubscribed). Harmless.
>
> **Partly done (2 Nov 2026):** the double subscription is fixed with a test, and the six cases are back and stable
> across two sweeps. The two flaky tests remain.
>
> **New (5 Nov 2026, not looked at yet):** `WizardExistingDbJoinTests.Step1a_JoinExistingDb_DoesNotOpenOrChangeD`
> (`readerMode: True`) failed once in a full-suite run: it expected one temp path and got two. It passed in three
> other full runs that day and when run alone or with its class. Guess (unconfirmed): something left in a shared temp
> folder by an earlier test, since tests run one at a time. Seen during collapse-days task 2, which doesn't touch the
> wizard.

---

## 9. Smaller fixes and housekeeping

> [!note]- **9.1 Campus look-ups by name and sort order silently fail** `decide`
> The campus queries ask for `$.Name` and `$.SortOrder`, but campus JSON is camelCase, so the queries get nothing back.
> As a result:
> - campuses are listed in no particular order;
> - the duplicate-name check never matches;
> - seeding from a `.tpconfig` file can create a duplicate campus.
>
> Fix the key case, then check the real databases for duplicate campuses before any clean-up. Campus order will change
> visibly (to the intended order).

> [!note]- **9.2 One rule for "do two meetings share a week?"**
> The conflict checks and the Room Browser expand "odd" and "even" to weeks 1–18 only, so an "odd" meeting against a
> list that includes week 21 shows no clash. The section import has its own, correct copy of the rule. Put the parity
> rule in the shared method, have the import use it, and delete the copy. Add a test for week 21. Harmless in a normal
> semester.

> [!note]- **9.3 The shared-schedule import should use the shared day and frequency helpers**
> It keeps its own day map and frequency check. Switch it to the helpers the section import uses. The input it accepts
> changes slightly (compact days such as `MWF`), so check that existing shared-schedule files still parse.

> [!note]- **9.4 Section import refuses semicolon- or tab-separated files**
> Such files are refused with a missing-header message.
> *Source: `designs/section-import-by-user.md`.*

> [!note]- **9.5 A section picked while the section list is hidden isn't scrolled into view**
> For example: pick a section on the grid while the Events list is showing, then switch back, and the card isn't
> revealed. Reveal it when the list is shown again (through the scroll-anchor behaviour, never by scrolling the list
> directly).

> [!note]- **9.6 Room Browser: test that every option is valid and none is missed**
> Add tests against the real-data database:
> 1. Every slot is valid across many scenarios.
> 2. Exact completeness for the "same room, same time" tier.
> 3. For the other tiers, which only sample, never "nothing available" when brute force finds an arrangement.
>
> Check from the raw data, not the solver's own index, or the tests repeat its mistakes.

> [!note]- **9.7 Review Avalonia 12.1.4's release notes**
> Published 2026-10-09. One fix touches the section card's property-line icons. Re-run the scroll-anchor probe after
> any upgrade.

> [!note]- **9.8 File an Avalonia bug for the list "jump"**
> Virtualizing the section list cut an 800-section heap from 475 MB to 160 MB, but it is blocked by a jump when an
> on-screen item grows. Build a tiny stand-alone repro, and Greg files the issue. About an hour, with no risk to the
> app.

---

## Decided against

- **A switch that overlaps a close can delete kept work.** Only the harness can reach it, so it is not pursued (Greg,
  2026-10-21). Recorded so it isn't rediscovered as new.
- **Merging stranded changes back into the database** (Bob's changes alongside Alice's, after he lost the lock): not
  for 1.2.4 (Greg, 2026-11-01). Recovered Versions (3.5) keeps his version "for reference". A merge would need the
  version Bob started from to be kept on disk (persistence review, sections 5 and 8.6).
- **Other ways to narrow the Schedule view** (Greg, 2026-11-04; tried with real data while designing 2.5 and 2.13):
  - *Cascading* overlapping tiles over each other's empty lower part: Monday 468 → 412 px. The widest groups start
    together, so there is little to gain.
  - *Notched tiles*, which drop a label below an earlier tile and keep a thin tab at the real start: 24% narrower,
    but start times read up to an hour late, and the tabs look like separate tiles in the shared fill.
  - *Fit the week to the window* by scaling: 0.56 for the narrowest slice, cutting course codes on 38% of lines.
  - *"+N more" tiles* for crowded times: the week fits only at two columns per day, which folds away 23–62% of
    sections.
  - *Turning the grid* (days as rows, time across) would fix the geometry, but it overturns the primary view.
    Parked, not rejected.

## Notes for tidying the spec file

- **Rows done in 1.2.3, still listed as pointers:** Load by subject, Section Import by User, the BugSnag scrubber
  and reports, and the heartbeat takeover fix.
- **Duplicate number:** two different entries are both "P10" (the macOS local-copy deletion, and part-semester
  sections). P11 and two unnumbered rows sit at the end of "Other items" with no notes.
- **1.2.4 items that aren't in the spec** (all included above): field-test open issues A3–A7, A9 and A10; the
  HANDOFF harness notes; "Subjects taught" (`designs/load-by-subject.md`); semicolon and tab imports
  (`designs/section-import-by-user.md`).
- **Designs in progress, not placed in a release:** Slot Browser and Access Analyzer (both at Phase 1). The Slot
  Browser spec relies on 2.10's reading of room type.
- **Before 1.2.4 work (HANDOFF):** the Microsoft support case; restart GregDeskTop and remove the sideload build and
  the test MSIX folders; check the Store update link now that 1.2.3 is certified; `master` is 7 doc commits ahead of
  `origin`.
