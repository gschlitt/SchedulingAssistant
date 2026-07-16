# Enhanced Imports — Filterable Shared Schedules

## Phase 1 — Problem Statement
**Status: LOCKED** — signed off 2026-07-15

### Who
University department schedulers who use TermPoint's existing shared-schedule
feature (CSV export/import) to overlay another department's sections on their
grid.

### Problem
Today, when department A imports a shared schedule from department B, every
section B exported appears on A's grid unconditionally. A's filter controls
have no effect on B's overlay sections because the CSV carries only scheduling
geometry (day, time, duration) and identifiers (course code, section code).
It does not carry any of the property dimensions that A's filters operate on:
instructor, room, campus, section type, tags, meeting type, or level.

This makes the overlay noisy. If A is filtering their own grid to "only
sections tagged Upper Level", B's sections still flood the view unfiltered.
The overlay loses its value precisely when A is trying to focus.

### Goals
**G1 — Filterable overlays.** Allow A to apply its own filter state to B's
imported sections, so that the overlay obeys the same focus that A has set
for their own schedule.

**G2 — Cross-department conflict detection.** Once shared sections carry
enriched property data (instructor names, room+building, tags), the
existing conflict detection systems should extend to cover them:
- **Room conflicts**: B's section in "Room 204 / Science Building" at 10am
  overlaps A's section in the same room at 10am — a real double-booking.
- **Instructor conflicts**: A cross-appointed instructor teaching in both
  departments at overlapping times.
- **Access watch conflicts**: B's exported sections that carry tags
  matching A's program watch criteria should participate in the overlap
  detection, surfacing student-access conflicts across departments.

These two goals reinforce each other. Filtering lets A focus the overlay;
conflict detection alerts A to genuine problems within that overlay.
Both require the same enriched CSV data.

### Design Direction — Uniform Representation

Shared sections should be represented as close to local sections as
possible, so that the filter, conflict detection, and access watch
systems operate uniformly without special-casing.

At import time, B's exported property names are resolved against A's
local entities (instructors, rooms, tags, etc.) using name-matching.
The result is a `Section` object (or equivalent) populated with A's
own entity IDs wherever a match is found. These resolved sections are
then fed into the same pipeline that processes A's own sections —
same filter predicates, same conflict detection, same access watch
evaluation.

The boundary between shared and local data is enforced not by a
parallel type hierarchy, but by *where the data lives*: shared
sections exist only in `SharedScheduleService`, never in
`SectionStore` or any repository. A flag or marker distinguishes them
for the few operations that must treat them differently: rendering
(purple styling), selection (no editor), and persistence (never saved).

This replaces the current approach where `SharedSection`,
`SharedMeeting`, and `SharedScheduleBlock` form a separate parallel
hierarchy that the grid pipeline must handle as a special case.

### Constraints

**C1 — Data isolation.** B's imported data must never touch A's database.
Today, shared sections live in memory only (via `SharedScheduleService`).
That remains true. Additionally, A must never be in a position to edit B's
data — not even the in-memory copy. The import is strictly read-only and
transient.

**C2 — No new filter items from imports.** Importing B's schedule must not
introduce new entries into any of A's filter drop-downs. If B has a tag
"Unit1" that A does not, "Unit1" must not appear in A's tag filter list.
B's data is an overlay on A's world — it does not expand A's vocabulary.

**C3 — No user-configured mapping.** If two departments wish to share
effectively, they agree on naming conventions for shared concepts (tags,
section types, campus names, instructor names). The system matches by
*display name* — case-insensitive, whitespace-normalized — not by database
GUID. There is no mapping UI.

**C4 — Course and Subject filters exempt.** Courses and subjects are
department-specific. Filtering by A's courses or subjects would exclude
everything B shared, which is never useful. When a Course or Subject filter
is active, B's sections pass automatically — those two dimensions do not
apply to shared data.

**C5 — All other filters apply symmetrically.** When A has a filter active
in any non-exempt dimension (instructor, room, campus, section type, tag,
meeting type, level), B's sections are evaluated by the same criteria. If B
exported a section tagged "Upper Level" and A has an "Upper Level" tag, it
matches. If A has no such tag, the section is excluded — there is no
"show unmatched" fallback.

**C6 — Enriched CSV format.** The export must be extended to include the
property names that filters need. Since GUIDs are database-local, all
exported values are *display names*. The format must be backward-compatible:
old importers should ignore new columns gracefully, and new importers should
handle CSVs that lack the new columns (treating missing properties as blank).

**C7 — Name-matching rules.** Matching between exported names and A's local
entities is:
- Case-insensitive
- Leading/trailing whitespace stripped
- Internal whitespace normalized (runs of whitespace collapse to a single space)

No fuzzy matching, no synonyms, no Levenshtein. Exact (after normalization)
or nothing.

### Design Tensions

**DT1 — Instructor name collisions.** Instructor names are not globally
unique. Department B may have two instructors named "John Smith".
Department A may also have a "John Smith" (possibly the same person, if
cross-appointed). When A filters by their "John Smith", all of B's sections
assigned to any "John Smith" will match. This is over-matching, but the
alternative — requiring some form of disambiguation or mapping — violates C3.

With conflict detection (G2), the stakes increase. A false-positive
*filter match* is cosmetic noise; a false-positive *instructor conflict*
is a spurious alert that could cause unnecessary schedule changes. The
disambiguation strategy needs to be good enough that conflict detection
is trustworthy, not just that filtering is roughly useful.

Possible mitigations to explore in later phases:
- Export instructor with a qualifying detail (e.g., initials, employee ID)
  that makes collisions rarer in practice
- Accept over-matching and document it: "if departments share instructor
  names, filtering will match all of them"
- Allow the export to carry an opaque instructor identifier alongside the
  name, so that B's two "John Smith" entries are at least distinguishable
  from each other (even if A can't tell which is which)
- The practical question: how common are same-name instructors across
  collaborating departments? If rare, over-matching is tolerable. If
  common, we need a lightweight tiebreaker that doesn't require mapping.

**DT2 — Multi-value fields.** A section can have multiple tags and multiple
instructors. The CSV format must represent these cleanly. Tags use AND
filtering in the existing system (a section must carry ALL selected tags to
pass). The same logic extends to shared sections unchanged: B's sections
are evaluated exactly as A's own sections would be.

Example: A filters by tag "Upper Level". B's section has tags
["Upper Level", "Unit1"]. The section passes — it carries the required
tag, same as one of A's own sections would. The presence of "Unit1"
(which A may not even have) is irrelevant; it's simply a tag that A
is not filtering by. No special "strip unrecognized" logic is needed.

The guiding principle: **filter evaluation is identical for local and
shared sections.** The only difference is that shared sections carry
property *names* instead of IDs, so a name-to-ID resolution step
happens before the same filter predicates run.

**DT3 — Room matching and disambiguation.** Rooms have two fields:
`Building` and `RoomNumber`. The CSV should export both, and matching
should use the composite `(Building, RoomNumber)` — not room number
alone. This provides natural disambiguation: "204" in "Science Building"
is a different room than "204" in "Arts Building".

When `Building` is empty on either side, matching falls back to
`RoomNumber` only, which risks over-matching. Users who encounter this
can resolve it by editing their rooms to include building names — a
progressive-fidelity approach that doesn't require upfront configuration.

With conflict detection (G2), room matching becomes safety-critical.
A false-positive room conflict ("you and Chemistry both have Room 204
at 10am") is actionable but wrong if the rooms are in different buildings.
The `(Building, RoomNumber)` composite makes this much less likely.
Empty buildings produce a genuine ambiguity — the conflict alert should
note when a match was made without building context so the user knows
to verify.

**DT4 — Backward compatibility.** Existing shared-schedule CSVs have 9
columns (CourseCode through Frequency). Enhanced CSVs will have additional
columns. The parser must handle both:
- Old CSV (no new columns) → shared sections carry no property metadata,
  so they are excluded by any active property filter (they have no tags,
  no instructor, etc.). This is the correct behavior: an unenhanced
  export simply can't participate in property filtering.
- New CSV read by old parser → old parser should ignore extra columns.
  The current parser is positional (column 0–8), so trailing columns are
  already ignored. No change needed on the old-parser side.

**DT5 — Access watch integration.** The program conflict detector
(`ProgramConflictService`) defines watches by tag or course. A watch
says "these sections serve the same student population — alert me if
they overlap." Today it operates only on A's own sections. With enriched
shared data, B's sections that carry matching tags could participate.

This is powerful: if A has a watch on tag "Pre-Med Required" and B's
exported Biology section carries that same tag, the system can flag
that A's Chemistry section and B's Biology section overlap — a genuine
student-access conflict across departments, which is exactly what the
original schedule-overlay feature was motivated to solve.

Course-mode watches don't apply to shared data (courses are
department-specific, per C4). Tag-mode watches are the natural fit.

Open question: should shared sections participate in existing watches
automatically (whenever their tags match), or should the user explicitly
opt watches into cross-department mode? Automatic is simpler and
probably correct — the watch says "any section with this tag", and a
shared section with that tag is just another section.

**DT6 — Conflict detection scope.** Three distinct conflict types
emerge from G2:
1. **Room conflicts** — A's section and B's section use the same room
   at overlapping times. Detected by matching `(Building, RoomNumber)`.
2. **Instructor conflicts** — A's section and B's section share an
   instructor at overlapping times. Detected by matching instructor name.
3. **Access watch conflicts** — A's section and B's section both carry
   a tag that an active program watch is monitoring, and they overlap.
   Detected by the existing `ProgramConflictService` algorithm, extended
   to include shared sections in its input set.

Each type has different false-positive characteristics. Room and access
watch conflicts are high-signal (if the names match, it's probably real).
Instructor conflicts depend heavily on name disambiguation (DT1).
Phase 3 (UX) will need to decide how to surface these — inline on the
grid, in a panel, or both.

### What "Done" Looks Like
1. B exports a shared schedule CSV that includes property display names
   (instructor, room+building, campus, section type, tags, meeting type,
   level) alongside the existing scheduling geometry.
2. A imports that CSV. Shared sections carry in-memory property name
   metadata (never persisted).
3. When A activates a filter (tag, instructor, room, campus, section type,
   meeting type, level), B's overlay sections are evaluated against that
   filter using name-matching. Sections that don't match are excluded from
   the grid, just like A's own sections would be.
4. Course and Subject filters do not apply to shared sections.
5. B's property names never appear in A's filter drop-downs.
6. Room conflicts, instructor conflicts, and access watch (tag-based
   program watch) conflicts are detected between A's sections and B's
   overlay sections, using the same name-matching resolution.
7. The overlay remains read-only, in-memory, and transient. Data isolation
   is preserved.

### What "Done" Does NOT Include
- Editing shared sections
- Persisting shared sections to A's database
- User-configured name mappings between departments
- Fuzzy or approximate name matching
- Any change to how A's own sections are filtered or how conflicts are
  detected among A's own sections

### Open Questions for Phase 2 — RESOLVED
1. **CSV column layout** — RESOLVED in Phase 2.
2. **Instructor disambiguation** — RESOLVED in Phase 2.
3. **Shared section representation** — RESOLVED in Phase 2 (uniform
   representation: resolved `Section` objects with `IsShared` flag).
4. **Tag AND-logic** — RESOLVED: no special logic. Apply the same AND
   filter to shared sections as to local sections. A tag that A doesn't
   filter by is simply irrelevant, whether A recognizes it or not.

---

## Phase 2 — Domain Model
**Status: LOCKED** — signed off 2026-07-15

### Enriched CSV Format

The export adds 8 columns after the existing 9, for a total of 17.
Existing columns are unchanged in position and semantics.

| # | Column | Scope | Cardinality | Format |
|---|--------|-------|-------------|--------|
| 1 | CourseCode | section | single | string |
| 2 | SectionCode | section | single | string |
| 3 | Notes | section | single | string |
| 4 | Day | meeting | single | day name |
| 5 | StartTime | meeting | single | "h:mm tt" |
| 6 | EndTime | meeting | single | "h:mm tt" |
| 7 | DurationMin | meeting | single | integer |
| 8 | StartMinutes | meeting | single | integer |
| 9 | Frequency | meeting | single | string |
| 10 | Instructor | section | multi | pipe-delimited (see below) |
| 11 | Initials | section | multi | pipe-delimited, order matches Instructor |
| 12 | Building | meeting | single | string |
| 13 | RoomNumber | meeting | single | string |
| 14 | Campus | section | single | name string |
| 15 | SectionType | section | single | name string |
| 16 | Tags | section | multi | pipe-delimited name strings |
| 17 | MeetingType | meeting | single | name string |
| 18 | Level | section | single | string (e.g. "100", "300") |

**Scope** indicates whether the value is per-section (repeated
identically on every row for that section) or per-meeting (varies
by row). The parser uses the first row's value for per-section fields
and ignores subsequent rows.

**Multi-value encoding**: pipe-delimited within a single CSV field.
Example: `"Smith, John|Doe, Jane"`. The pipe character `|` is chosen
because it is rare in names and unambiguous within RFC-4180 quoted
fields.

**Instructor format**: `"LastName, FirstName"` per entry. The comma
between last and first name is inside a quoted CSV field, so it does
not conflict with the CSV column delimiter. Multiple instructors are
pipe-delimited: `"Smith, John|Doe, Jane"`.

**Initials**: exported in a separate column, pipe-delimited in the
same order as the Instructor column. This is needed for grid display:
when an instructor doesn't resolve to one of A's local instructors,
the tile still needs initials to render. Keeping initials in a
separate column (rather than appended to the name) avoids polluting
the name-matching field.

**Building and RoomNumber**: exported as separate columns (columns 12
and 13) rather than combined, because matching uses the composite
`(Building, RoomNumber)` and the fields are stored separately in the
`Room` model. Per-meeting scope, since each `SectionDaySchedule` can
have a different room.

**MeetingType**: per-meeting scope, since each `SectionDaySchedule`
can have a different meeting type.

**Level**: exported directly as the string already stored on the
section (e.g. "100"). No resolution needed — level filtering is a
plain string comparison.

#### Example

```
#TermPoint Schedule Overlay,Chemistry Department,Fall 2026,2026-07-15
CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency,Instructor,Initials,Building,RoomNumber,Campus,SectionType,Tags,MeetingType,Level
CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,204,Main Campus,Lecture,Upper Level|Pre-Med Required,In Person,100
CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,204,Main Campus,Lecture,Upper Level|Pre-Med Required,In Person,100
CHEM101,A,,Friday,8:00 AM,8:50 AM,50,480,,"Smith, John|Doe, Jane",JRS|JD,Science Building,110,Main Campus,Lecture,Upper Level|Pre-Med Required,Lab,100
CHEM201,B,Prereq: CHEM101,Tuesday,1:00 PM,2:20 PM,80,780,,"Doe, Jane",JD,Arts Building,301,Main Campus,Lecture,Upper Level,,200
```

Note: CHEM101 section A meets MWF, with a different room on Friday
(110 vs 204) and a different meeting type (Lab vs In Person). The
per-section fields (Instructor, Initials, Campus, SectionType, Tags,
Level) repeat identically on all three rows.

#### Backward Compatibility

- **New importer reading old CSV**: columns 10–18 are absent. The
  parser sees no Instructor, Room, etc. The resulting `Section` objects
  have empty instructor assignments, no room, no tags, etc. They behave
  as "unassigned" in every property dimension — any active property
  filter excludes them. This is the correct degraded behavior.
- **Old importer reading new CSV**: the current parser uses a
  column-name-to-index dictionary and only reads columns it recognizes.
  Extra columns are ignored. No change needed.

### Name-Matching Resolution

At import time, each exported name string is resolved against A's
local entities. Resolution happens once, at import, producing a
`Section` object with A's entity IDs. The filter and conflict
pipelines then operate on IDs, just as they do for local sections.

#### Normalization (C7)

Before any comparison, both the exported name and A's entity name are
normalized:
1. Trim leading/trailing whitespace
2. Collapse internal whitespace runs to a single space
3. Compare case-insensitively (ordinal)

This is applied uniformly to all name-matching dimensions.

#### Resolution Rules by Dimension

**Instructor** — match on `(LastName, FirstName)` with initials as
tiebreaker.
- A's instructors are indexed by normalized `(LastName, FirstName)`.
  Multiple instructors may share the same key (same name).
- Each exported `"LastName, FirstName"` entry is looked up in the index.
- If exactly one match: resolve to A's instructor ID. Populate the
  `InstructorAssignment` with that ID.
- If zero matches: no ID. The section carries the exported name and
  initials for display purposes only. The instructor dimension treats
  this section as "unstaffed" for filtering. For conflict detection,
  no instructor conflict can be raised (no local instructor to
  conflict with).
- If multiple matches (A has two or more instructors with identical
  first+last name): use the exported Initials to disambiguate. If
  exactly one of A's matches has initials that match the exported
  initials (case-insensitive, trimmed), resolve to that one. If the
  initials tiebreaker still leaves multiple matches or no matches,
  resolve to none and log a warning — the ambiguity is genuine and
  the user should adjust their instructor names or initials to differ.

**Instructor disambiguation decision (DT1)**: primary match on
`(LastName, FirstName)`, with initials as tiebreaker when multiple
instructors share the same name. This handles the common case cleanly
(one match → resolved) and the uncommon case reasonably (two "John
Smith" instructors → initials disambiguate). If the same person is
cross-appointed in both departments and named identically, the match
is correct. If two different people share a name but have different
initials, the tiebreaker resolves correctly. If they share both name
and initials, the ambiguity is genuine — no resolution, warning
logged, user adjusts names to differ per C3.

**Room** — match on `(Building, RoomNumber)` composite.
- A's rooms are indexed by normalized `(Building, RoomNumber)`.
- Both fields must match for a hit (when both are non-empty).
- When the exported Building is empty: match on RoomNumber alone.
  If A has exactly one room with that number (across all buildings),
  resolve to it. If A has multiple rooms with that number in
  different buildings, no resolution (ambiguous). Log a warning.
- When A's room has no Building: same logic in reverse.
- Resolved room ID is set on the `SectionDaySchedule.RoomId`.

**Campus** — match on `Name`.
- A's campuses (SchedulingEnvironmentValues of type "campus") are
  indexed by normalized Name.
- Resolved ID set on `Section.CampusId`.

**Section Type** — match on `Name`.
- A's section types (SchedulingEnvironmentValues of type "sectionType")
  indexed by normalized Name.
- Resolved ID set on `Section.SectionTypeId`.

**Tags** — match each tag name individually on `Name`.
- A's tags (SchedulingEnvironmentValues of type "tag") indexed by
  normalized Name.
- Each exported tag that matches adds A's tag ID to `Section.TagIds`.
- Unmatched tag names are silently dropped from the resolved section.
  (They don't participate in filtering or access watch — but they
  never existed in A's world, so this is correct.)

**Meeting Type** — match on `Name`.
- A's meeting types (SchedulingEnvironmentValues of type "meetingType")
  indexed by normalized Name.
- Resolved ID set on `SectionDaySchedule.MeetingTypeId`.

**Level** — no resolution. The exported string is stored directly on
`Section.Level`. Level filtering uses string comparison, not ID lookup.

#### Resolution Index

A single `ImportResolutionIndex` (or equivalent) is built once from
A's local data at the start of import. It holds:
- `Dictionary<(string LastName, string FirstName), List<Instructor>>` —
  keyed by normalized names (list to handle same-name instructors;
  initials tiebreak when list has multiple entries)
- `Dictionary<(string Building, string RoomNumber), Room>` — keyed
  by normalized composite
- `Dictionary<string, SchedulingEnvironmentValue>` — per type (campus,
  sectionType, tag, meetingType), keyed by normalized Name

This keeps the resolution logic clean and testable: build the index
from A's data, then resolve each imported row against it.

### Model Changes

#### Section — transient display fields

`Section` gains several `[JsonIgnore]` properties that are populated
only for shared sections. They are never persisted.

| Property | Type | Purpose |
|----------|------|---------|
| `IsShared` | `bool` | Defense-in-depth flag. True for imported shared sections. Prevents edit/save operations. Default false. |
| `SourceLabel` | `string?` | E.g. "Chemistry Department". For display in the grid strip and tooltips. |
| `DisplayCourseCode` | `string?` | The exported CourseCode string, for grid tile rendering. Local sections use CourseId → lookup; shared sections use this directly. |
| `DisplayInstructors` | `List<(string Name, string Initials)>?` | Exported instructor names and initials for all instructors on this section — both resolved and unresolved. For tile rendering (initials) and tooltips (full names). |
| `RoomConflictNote` | `string?` | Room conflict annotation, populated by `DetectConflicts` output. Surfaces in tile tooltips and the shared schedule strip. |
| `InstructorConflictNote` | `string?` | Instructor conflict annotation, populated by `DetectConflicts` output. Surfaces in tile tooltips and the shared schedule strip. |

These fields give the renderer everything it needs to display shared
sections — including conflict state — without looking up entities
that don't exist in A's database.

#### SharedScheduleSet — holds resolved Sections

```
SharedScheduleSet
├── SourceLabel: string
├── ExportedAt: DateTime?
├── Sections: List<Section>          // ← was List<SharedSection>
└── ResolutionSummary: ImportResolutionSummary
```

`Sections` now holds fully resolved `Section` objects with `IsShared = true`.

`ImportResolutionSummary` captures what the resolution step found, for
display in the import status message and the shared schedule strip:
- `ResolvedInstructors: int` — how many instructor names matched
- `UnresolvedInstructors: int` — how many did not
- `ResolvedRooms: int` / `UnresolvedRooms: int`
- `ResolvedTags: int` / `UnresolvedTags: int`
- `Warnings: List<string>` — e.g. "Ambiguous room match: 204 exists
  in multiple buildings"

#### Retired Models

The following models become obsolete and will be removed:
- `SharedSection` — replaced by `Section` with `IsShared = true`
- `SharedMeeting` — replaced by `SectionDaySchedule`
- `SharedScheduleBlock` — replaced by `SectionMeetingBlock` with
  `IsShared` flag (from the section it was built from)

The grid pipeline no longer needs a separate "Pass 4" for shared blocks.
Shared sections are simply included in the section list that the
existing passes process.

### Data Isolation Invariants

These invariants enforce C1 at the model layer:

1. **Synthetic IDs.** Shared sections receive a fresh `Guid.NewGuid()`
   ID at import time. These IDs are ephemeral (not persisted) and
   cannot collide with A's real section IDs.

2. **SemesterId.** Shared sections are stamped with the current
   semester's ID so they participate in semester-scoped pipeline
   operations. They are dismissed on semester change (existing
   behavior, retained).

3. **Collection boundary.** Shared sections exist only in
   `SharedScheduleService.Sets[].Sections`. They are never added to
   `SectionStore`, never passed to any repository, never included in
   any save/export-of-A's-data operation.

4. **IsShared flag.** Any code that receives a `Section` and performs
   a write operation (save, update, delete, copy) must check
   `IsShared` and refuse. This is defense-in-depth behind the
   collection boundary.

5. **No CourseId.** Shared sections have `CourseId = null`. Any code
   that requires a CourseId (e.g. section code uniqueness checks,
   course-level operations) naturally excludes them.

### Open Questions for Phase 3 — RESOLVED
1. **Grid rendering** — RESOLVED in Phase 3.
2. **Shared schedule strip** — RESOLVED in Phase 3.
3. **Conflict rendering** — RESOLVED in Phase 3.
4. **Import feedback UX** — RESOLVED in Phase 3.

---

## Phase 3 — UX Sketch
**Status: LOCKED** — signed off 2026-07-15

This phase describes what the user sees and does. It covers the
export workflow, import workflow, grid rendering, filtering behavior,
conflict presentation, the shared schedule strip, and tooltips.

### Export Workflow

Unchanged from the user's perspective. The exporter writes more
columns, but the workflow is the same:
1. User sets filters to the sections they want to share.
2. User opens Sharing flyout → Export.
3. File picker opens (defaulting to the shared folder if set).
4. CSV is written with the enriched column set.

The only visible change: the export status message could mention the
enriched format ("Exported 12 sections with properties to
Chemistry Fall 2026.csv"). This reassures the user that the new
columns are being written.

### Import Workflow

The import workflow gains an import resolution step, but the user
action is the same: open Sharing flyout → Import → pick file.

What changes is the **feedback after import**. The current status
message is either:
- "Imported 12 sections from Chemistry Department."
- "Imported 10 of 12 rows (2 skipped)."

With resolution, the status message should communicate what matched:

**Primary status message** (always shown):
> "Imported 12 sections from Chemistry Department.
> Matched: 3 instructors, 8 rooms, 4 tags."

This tells the user which properties resolved and gives them a sense
of how much filtering/conflict power they have.

**Warning details** (shown when there are unresolved properties or
ambiguities): A secondary line or expandable detail below the
primary message:
> "1 instructor unresolved (Martinez, Carlos).
> 2 tags not in your system (Unit1, LabStream)."

This is informational, not blocking — the import succeeds regardless.
Unresolved properties just mean those sections won't participate in
that filter dimension.

The implementation should use the existing `StatusMessage` property
on `SharingViewModel`, extended to support multi-line or styled
text. If the detail is too long for the flyout, a "Details…" link
could open a simple dialog or expand a section, but keeping it
inline is preferred.

### Grid Rendering

#### Tile Appearance (unchanged visual language)

Shared tiles retain their current visual identity:
- **Purple text** (`SharedScheduleText` brush) — immediately
  distinguishes shared from local sections
- **Standard tile border** — no special border needed; the purple
  text is sufficient differentiation
- **Selectable** — clicking a shared tile sets selection on its
  synthetic section ID, highlighting it on the grid and in the
  workload panel. This gives the user cross-panel visual feedback
  without any risk of editing. Selection does not open the section
  editor (that requires clicking in the section list, which does
  not contain shared sections). The current `IsCommitment = true` /
  `SectionId = string.Empty` implementation is replaced by
  `IsCommitment = false` with the section's real synthetic ID.
- **No context menu** — right-clicking a shared tile does nothing
  (context menu actions are all edit operations).
- **No flag icon** — shared sections don't carry flags.

#### Tile Content (enriched)

Today, shared tiles show only `"CHEM101 A"` with no initials.
With the enriched data, tiles can show instructor initials:

> `CHEM101 A  JRS JD`

The initials come from two sources:
- **Resolved instructors**: use A's instructor's `Initials` field
  (same as local sections).
- **Unresolved instructors**: use the initials from the CSV
  (`DisplayInstructors` on the section).

The `BuildSectionLabel` method already concatenates initials for
local sections. For shared sections, it should follow the same
pattern but pull from the display initials when no local instructor
ID exists.

#### Filter Behavior

When a filter is active, shared sections are evaluated by the same
predicates as local sections (per Phase 1, C5). The grid pipeline
change is:

**Today**: shared blocks are added in a separate pass
(`BuildSharedScheduleBlocks`) after filtering, so they bypass all
filters.

**Enhanced**: shared sections are included in the main section list
*before* `BuildFilteredBlocks` runs. They carry resolved IDs, so the
existing filter predicates evaluate them naturally. No separate pass
needed.

**Course and Subject exemption (C4)**: the filter predicates for
Course and Subject dimensions must skip sections with `IsShared = true`.
These sections pass those two dimensions automatically. All other
dimensions apply normally.

**No filter pollution (C2)**: `PopulateFilterOptions` builds the
filter drop-down lists from `GridLookups`, which is built from
A's local data (sections, instructors, rooms, etc.). Shared sections
are not included in the lookups that feed filter options — only in
the section list that the filter evaluates. This is the mechanism
that prevents B's property names from appearing in A's filter
drop-downs.

#### Co-scheduling and Overlap

Shared sections participate in the existing `ComputeTiles` logic:
- **Co-scheduling**: if a shared section and a local section have
  identical start time and duration, they share a tile (stacked
  with a thin rule). The shared entry renders in purple text within
  the shared tile. This is correct and useful — it shows that both
  departments have a section at the same time.
- **Overlap**: if a shared section and a local section overlap but
  aren't co-scheduled, they appear side-by-side in the same day
  column. This is also correct — standard overlap rendering.

No special-casing needed; the existing tiling algorithm handles
`SectionMeetingBlock` uniformly.

### Conflict Detection

The overriding principle: **minimal change.** To the user, shared
sections should behave the same way their own sections do. Room and
instructor conflicts already surface in the section list — shared
sections should participate in that same mechanism.

#### Room and Instructor Conflicts

`RoomConflictService.DetectConflicts` and
`InstructorConflictService.DetectConflicts` both take
`IReadOnlyList<Section>`. Today, that list comes from
`SectionStore.SectionsBySemester`. With the uniform representation,
shared sections (resolved `Section` objects with A's room/instructor
IDs) are simply included in the input list alongside A's own
sections.

The result: if B's CHEM101 A uses Room 204 at 8:00 AM on Monday,
and A's HIST101 A also uses Room 204 at 8:00 AM on Monday, a room
conflict warning appears on A's HIST101 A card in the section list —
exactly as it would if the conflict were between two of A's own
sections. The warning text references B's section by course code
(from `DisplayCourseCode`).

Conflicts are visible from **both sides**:
- A's sections receive conflict warnings on their section list
  cards (via `SectionListItemViewModel.RoomConflictWarning` /
  `InstructorConflictWarning`), as they do today.
- B's shared sections receive conflict annotations via
  `[JsonIgnore]` display properties on `Section` itself
  (`RoomConflictNote`, `InstructorConflictNote`), populated from
  the same `DetectConflicts` output. These annotations are
  available to any view that renders shared sections: the grid
  tile tooltip, the shared schedule strip, and the teal glow on
  the grid tile.

This is symmetric: the user sees the conflict flagged on both
their own section and the shared section that causes it.

#### Access Watch Conflicts

`ProgramConflictService.DetectConflicts` takes a list of sections
and evaluates tag-mode watches. Shared sections with resolved tag
IDs participate automatically — no opt-in needed. If B's section
carries a tag that matches one of A's program watches, and it
overlaps with one of A's sections under that watch, the conflict
appears in the access panel with its teal glow on the grid. Same
visual language, same panel, same behavior.

#### Grid conflict rendering

The teal radial glow behind conflicting tile entries applies to
shared tile entries the same way it applies to local ones. The
renderer uses `conflictSectionDays` built from
`ProgramConflictService` output, which now includes shared sections.
No rendering change needed.

### Shared Schedule Strip

The collapsible strip above the grid currently shows:
- **Collapsed**: `"Chemistry Dept (12) · Biology (8)"`
- **Expanded**: per-source listings with course code, section code,
  and schedule

#### Enhanced collapsed summary

Add resolution stats:
> `"Chemistry Dept (12, 3/4 instr) · Biology (8, all matched)"`

Keep it concise — the collapsed summary is a glance indicator, not
a report. Room/instructor conflicts are surfaced in the section list
where the user already looks for them.

#### Enhanced expanded view

Each source group shows:

**Header row**: source label, export date, resolution summary
> Chemistry Department — exported 2026-07-15
> Matched: 3/4 instructors · 8/8 rooms · 4/6 tags

**Section rows** (as today, but enriched):
> `CHEM101 A  JRS JD  MWF 8:00–8:50 AM  Rm 204 Sci`
> `CHEM201 B  JD      TR 1:00–2:20 PM   Rm 301 Arts`

Adding instructor initials and room shorthand to the section rows
makes the expanded strip more useful as a reference.

### Section List

Shared sections **appear in the section list**, consistent with
the uniform representation principle. They look like local
sections with a few differences:

#### Rendering
- **Purple text** — matching the grid's shared schedule color,
  immediately distinguishes shared from local sections.
- **Source label** — a subtle indicator showing which department
  the section came from (e.g., "(Chemistry)" or a purple badge
  with the source label). Keeps the card compact but attributable.
- **Summary row** — shows course code, section code, instructor
  initials, and schedule lines, same as local sections. Uses
  `DisplayCourseCode` and `DisplayInstructors` since there's no
  local course or instructor to look up.

#### Interaction
- **Selectable** — clicking a shared section card highlights it
  across the grid and workload panel, same as local sections.
- **Not expandable** — clicking does not open the inline editor.
  There is nothing to edit. The card stays in summary-only mode.
- **No context menu** — right-click does nothing (all context
  menu actions are edit operations: copy, delete, flag, etc.).

#### Conflict warnings
Shared section cards display room and instructor conflict
warnings, using the `RoomConflictNote` / `InstructorConflictNote`
properties from the `Section` model. The warning renders the same
way it does on local section cards — consistent visual language.

#### Filter behavior
Shared section cards respond to the active filter highlight the
same way local cards do: highlighted when they pass the filter,
unhighlighted when they don't. Course/Subject filters exempt them
per C4.

#### Ordering
Shared sections are listed **after** all of A's local sections,
not interleaved with them. Within the shared section group, the
same sort order applies (by course code, then section code).
If multiple sources are loaded (e.g., Chemistry and Biology),
shared sections are sub-grouped by source, each group sorted
internally.

This keeps A's working list uncluttered at the top and the
imported overlay clearly separated below. The sort algorithm
needs a revision to partition local vs. shared before applying
the existing sort within each partition.

### Workload Panel

The workload panel shows what each instructor is doing. For a
cross-appointed instructor who appears in both A's database and
B's shared import, the panel should show the complete picture —
both A's sections and B's shared sections under that instructor.

#### How it works

`WorkloadPanelViewModel.Load()` already reads sections from
`_sectionStore.SectionsBySemester`. With enhanced imports, it also
reads shared sections from `SharedScheduleService`, merges them
into the section list, and passes the combined list to
`BuildItemsForInstructor`. Since shared sections are `Section`
objects with resolved instructor IDs, the existing
`InstructorAssignments` loop picks them up automatically.

#### Rendering

Shared section chips are visually distinct:
- Purple text (matching the grid's shared schedule color)
- Source label suffix, e.g., "CHEM101 A (Chem)" — so the user
  can see at a glance which sections come from which department
- Non-clickable — no selection, no navigation to the section
  editor (there is nothing to edit)

#### Workload totals

Shared sections do **not** contribute to workload totals. They
are informational context, not A's workload. The chip is shown
but its workload value is excluded from the instructor's semester
total and academic year total. The academic year total calculation
(which reads from `_sectionRepo` directly) naturally excludes
shared sections since they're never persisted.

#### Instructor list

Only A's instructors appear as rows. If B exports a section with
an instructor who doesn't resolve to any of A's instructors, that
section doesn't appear in the workload panel (there's no row for
it). This is correct — the workload panel is about A's people.

### Sharing Flyout

The sharing flyout is largely unchanged:
- **Import** button — same workflow, enhanced feedback (see above)
- **Export** button — same workflow, enriched CSV output
- **Set Shared Folder** — unchanged
- **Dismiss All** — unchanged
- **Loaded summary** — enhanced to include resolution stats:
  > "Loaded: Chemistry Dept (12, 3/4 instr matched) ·
  > Biology (8, all matched)"

### Open Questions for Phase 4 — RESOLVED
1. **Resolution index data sources** — RESOLVED in Phase 4.
2. **Conflict detection integration** — RESOLVED in Phase 4.
3. **Database schema** — RESOLVED in Phase 4.
4. **CSV column ordering in the DB** — RESOLVED in Phase 4.

---

## Phase 4 — Data Design
**Status: LOCKED** — signed off 2026-07-15

This phase specifies the data structures, repository access patterns,
and integration points that the import resolution, enriched export,
and conflict detection systems use. No database schema changes are
needed — shared data remains entirely in memory.

### Single-Semester Constraint

Shared schedule import and export are only available when exactly
one semester is selected. In multi-semester mode, the Import and
Export buttons are disabled (greyed out with a tooltip: "Select a
single semester to use shared schedules"). Dismiss All remains
available regardless.

**Why:** Multi-semester mode doubles the grid's complexity. Layering
shared schedules on top raises thorny questions — which semester
column do they appear in? Do they duplicate across both? How do
conflict detection and section list sorting handle the cross-product?
In practice, departments sharing schedules are looking at the same
single semester. The complexity isn't worth the edge case.

**Consequences:**
- The exporter writes the active semester's name into the CSV
  header comment (new field): e.g.
  `#TermPoint Schedule Overlay,Chemistry Department,Fall 2026,2026-07-15`
- The importer reads this field and stamps all shared sections with
  A's active semester ID. If the CSV's semester name doesn't match
  A's active semester name, the import status message includes a
  warning: "Note: this file was exported from Fall 2026 but you
  are editing Spring 2027."
- `BuildSharedScheduleBlocks` no longer loops over multiple
  semesters — there is only one.
- Section list ordering is simpler: shared sections appear after
  local sections within the single semester, sub-grouped by source.
  No cross-semester partitioning needed.
- Conflict detection merges shared sections into one semester's
  section list, not multiple.

### Resolved: Open Questions from Phase 3

**Q1 — Resolution index data sources.** The `ImportResolutionIndex`
is built from the same repositories that `GridLookups` uses, but
inverted: keyed by normalized display name instead of by entity ID.
It needs full entity lists from each repository's `GetAll()` method
(not just name→ID pairs) because the tiebreaker logic for instructors
needs `Initials`, and room matching needs both `Building` and
`RoomNumber`.

**Q2 — Conflict detection integration.** All three conflict services
(`RoomConflictService`, `InstructorConflictService`,
`ProgramConflictService`) accept `IReadOnlyList<Section>`. No
modification to the services themselves is needed. The call sites
merge shared sections into the input list before calling. The only
adjustment is that `courseCodeById` (used for display labels in
conflict descriptions) must include shared sections — keyed by the
shared section's synthetic ID, with the value from
`DisplayCourseCode`.

**Q3 — Database schema.** Confirmed: no new tables or columns. Shared
data lives exclusively in `SharedScheduleService._sets` (in-memory
`List<SharedScheduleSet>`). The `Section` model gains `[JsonIgnore]`
transient properties (specified in Phase 2) that are never serialized.

**Q4 — Exporter data access.** The exporter receives an
`ExportLookups` record — a lightweight subset of the dictionaries
that `GridLookups` holds, keyed by entity ID. `SharingViewModel`
builds this from the same repositories it already injects, plus
the additional repositories it needs for the enriched columns.

### ImportResolutionIndex

A single-use, immutable lookup structure built at the start of each
import from A's local entity repositories. Its purpose is to resolve
exported display names to A's entity IDs.

```
ImportResolutionIndex
├── Instructors: Dictionary<(string NormLast, string NormFirst), List<Instructor>>
├── Rooms: Dictionary<(string NormBuilding, string NormRoomNumber), Room>
├── RoomsByNumber: Dictionary<string NormRoomNumber, List<Room>>
├── Campuses: Dictionary<string NormName, Campus>
├── SectionTypes: Dictionary<string NormName, SchedulingEnvironmentValue>
├── Tags: Dictionary<string NormName, SchedulingEnvironmentValue>
└── MeetingTypes: Dictionary<string NormName, SchedulingEnvironmentValue>
```

#### Data sources

| Dimension | Repository method | Index key |
|-----------|------------------|-----------|
| Instructor | `IInstructorRepository.GetAll()` | `(Normalize(LastName), Normalize(FirstName))` → `List<Instructor>` (list because same-name instructors are possible) |
| Room | `IRoomRepository.GetAll()` | Primary: `(Normalize(Building), Normalize(RoomNumber))` → `Room`. Fallback: `Normalize(RoomNumber)` → `List<Room>` (for when exported Building is empty) |
| Campus | `ICampusRepository.GetAll()` | `Normalize(Name)` → `Campus` |
| Section type | `ISchedulingEnvironmentRepository.GetAll("sectionType")` | `Normalize(Name)` → `SchedulingEnvironmentValue` |
| Tag | `ISchedulingEnvironmentRepository.GetAll("tag")` | `Normalize(Name)` → `SchedulingEnvironmentValue` |
| Meeting type | `ISchedulingEnvironmentRepository.GetAll("meetingType")` | `Normalize(Name)` → `SchedulingEnvironmentValue` |

#### Normalization function

```
Normalize(string? s) → string
  1. If null or empty → return ""
  2. Trim leading/trailing whitespace
  3. Collapse internal whitespace runs to single space
  4. Return result (comparison is OrdinalIgnoreCase)
```

All dictionary lookups use `StringComparer.OrdinalIgnoreCase` (for
single-key dictionaries) or a custom `IEqualityComparer` that applies
ordinal-ignore-case to each tuple element (for composite keys).

#### Construction

`ImportResolutionIndex` is constructed by a static factory method:

```
ImportResolutionIndex.Build(
    IReadOnlyList<Instructor> instructors,
    IReadOnlyList<Room> rooms,
    IReadOnlyList<Campus> campuses,
    IReadOnlyList<SchedulingEnvironmentValue> sectionTypes,
    IReadOnlyList<SchedulingEnvironmentValue> tags,
    IReadOnlyList<SchedulingEnvironmentValue> meetingTypes)
```

The caller (`SharingViewModel` or the parser orchestration layer)
calls each repository's `GetAll()` and passes the entity lists in.
The factory builds all the normalized dictionaries.

This keeps the index decoupled from DI — it receives plain data and
is independently testable.

#### Collision handling during index construction

- **Instructors**: same `(LastName, FirstName)` maps to multiple
  entries in the `List<Instructor>` — this is expected and handled
  by the initials tiebreaker at resolution time.
- **Rooms**: same `(Building, RoomNumber)` with different IDs is a
  data error in A's system (duplicate rooms). Last-write-wins in the
  dictionary; log a warning. Same `RoomNumber` across different
  buildings is normal — the `RoomsByNumber` fallback dictionary
  holds all of them.
- **All others** (campus, section type, tag, meeting type): names
  are unique within each type by application invariant. If two
  entries have the same normalized name (data error), last-write-wins;
  log a warning.

### ImportResolutionSummary

Captures the outcome of the resolution step, for display in the
import status message and shared schedule strip.

```
ImportResolutionSummary
├── ResolvedInstructorCount: int
├── UnresolvedInstructorCount: int
├── ResolvedRoomCount: int
├── UnresolvedRoomCount: int
├── ResolvedTagCount: int
├── UnresolvedTagCount: int
├── ResolvedCampus: bool
├── ResolvedSectionType: bool
├── ResolvedMeetingTypeCount: int
├── UnresolvedMeetingTypeCount: int
└── Warnings: List<string>
```

"Resolved" counts are unique names that matched. "Unresolved" counts
are unique names that did not. Example: if 4 sections reference
"Smith, John" and the name resolves, that's 1 resolved instructor
(not 4). Counts reflect distinct names, not occurrences.

`ResolvedCampus` and `ResolvedSectionType` are booleans rather than
counts because they're per-section scalars — either the one exported
value matched or it didn't. (Multiple sections may share the same
campus/section-type value, but the summary cares about distinct
values, and in practice there's usually only one per export.)

Warnings are human-readable strings for edge cases:
- "Ambiguous instructor: Smith, John matches 2 instructors with
  identical initials"
- "Ambiguous room: 204 exists in Science Building and Arts Building"

### Section Model Changes

The `[JsonIgnore]` transient properties specified in Phase 2 are
implemented as simple auto-properties with no backing logic:

```csharp
[JsonIgnore] public bool IsShared { get; init; }
[JsonIgnore] public string? SourceLabel { get; init; }
[JsonIgnore] public string? DisplayCourseCode { get; init; }
[JsonIgnore] public List<(string Name, string Initials)>? DisplayInstructors { get; init; }
[JsonIgnore] public string? RoomConflictNote { get; set; }
[JsonIgnore] public string? InstructorConflictNote { get; set; }
```

`IsShared`, `SourceLabel`, `DisplayCourseCode`, and
`DisplayInstructors` use `init` — set once at import, never mutated.
`RoomConflictNote` and `InstructorConflictNote` use `set` — populated
after conflict detection runs, which happens after import.

These properties default to `false` / `null` for local sections and
have zero cost (no serialization, no allocation, no behavioral change)
on existing code paths.

### SharedScheduleSet Changes

```
SharedScheduleSet
├── SourceLabel: string
├── ExportedAt: DateTime?
├── Sections: List<Section>                  // ← was List<SharedSection>
└── ResolutionSummary: ImportResolutionSummary
```

`ResolutionSummary` is populated by the import resolution step and
read by the UI layer for status messages and the shared schedule
strip.

### ExportLookups

A lightweight record that bundles the ID→name dictionaries the
enriched exporter needs. Mirrors a subset of `GridLookups` but
contains only the dimensions relevant to export.

```
ExportLookups
├── InstructorsById: IReadOnlyDictionary<string, Instructor>
├── RoomsById: IReadOnlyDictionary<string, Room>
├── CampusesById: IReadOnlyDictionary<string, Campus>
├── SectionTypesById: IReadOnlyDictionary<string, SchedulingEnvironmentValue>
├── TagsById: IReadOnlyDictionary<string, SchedulingEnvironmentValue>
└── MeetingTypesById: IReadOnlyDictionary<string, SchedulingEnvironmentValue>
```

All keyed by entity `Id`, same as `GridLookups`. The exporter uses
these to resolve section IDs to display names for the enriched CSV
columns.

#### Construction

`SharingViewModel` builds `ExportLookups` from repository calls:

```
InstructorsById  ← IInstructorRepository.GetAll().ToDictionary(i => i.Id)
RoomsById        ← IRoomRepository.GetAll().ToDictionary(r => r.Id)
CampusesById     ← ICampusRepository.GetAll().ToDictionary(c => c.Id)
SectionTypesById ← ISchedulingEnvironmentRepository.GetAll("sectionType").ToDictionary(v => v.Id)
TagsById         ← ISchedulingEnvironmentRepository.GetAll("tag").ToDictionary(v => v.Id)
MeetingTypesById ← ISchedulingEnvironmentRepository.GetAll("meetingType").ToDictionary(v => v.Id)
```

This requires injecting `IInstructorRepository`, `IRoomRepository`,
`ICampusRepository`, and `ISchedulingEnvironmentRepository` into
`SharingViewModel` (in addition to the existing `ISectionRepository`
and `ICourseRepository`).

#### Shared construction with ImportResolutionIndex

Both `ExportLookups` and `ImportResolutionIndex` draw from the same
six repository calls. When export and import happen in the same
session (common — the user exports their own schedule and imports
a peer's), the repository data is fetched twice. This is fine:
each call returns ~tens to low hundreds of entities, the
repositories read from an in-memory SQLite cache, and the calls
are synchronous (< 1ms each). No shared caching layer is needed.

If a future optimization is desired, `SharingViewModel` could hold
the raw entity lists and rebuild both structures from them on demand.
But this is premature — the current cost is negligible.

### Enriched Exporter

#### Signature

The exporter's `Export` method gains an `ExportLookups` parameter:

```csharp
public string? Export(
    Stream output,
    string sourceLabel,
    IReadOnlyList<Section> sections,
    Func<string, string> courseCodeLookup,
    ExportLookups lookups)
```

The existing `courseCodeLookup` stays as-is (it's already built by
`SharingViewModel` from `ICourseRepository`). The new `lookups`
parameter carries everything else.

#### Column resolution logic

For each section, the exporter resolves IDs to display strings once,
then repeats the per-section values on every meeting row for that
section. Per-meeting values are resolved per row.

**Per-section columns** (computed once, repeated on every row):

| Column | Resolution | Format |
|--------|-----------|--------|
| Instructor | `section.InstructorAssignments` → for each assignment, look up `lookups.InstructorsById[assignment.InstructorId]`. Format each as `"LastName, FirstName"`. | Pipe-joined: `"Smith, John\|Doe, Jane"` |
| Initials | Same instructor lookups, same order → `instructor.Initials` for each. | Pipe-joined: `"JRS\|JD"` |
| Campus | `lookups.CampusesById[section.CampusId]?.Name` | Single string |
| SectionType | `lookups.SectionTypesById[section.SectionTypeId]?.Name` | Single string |
| Tags | `section.TagIds` → for each, `lookups.TagsById[id]?.Name`. Drop nulls (tag deleted since assignment). | Pipe-joined: `"Upper Level\|Pre-Med Required"` |
| Level | `section.Level` | Direct — no lookup |

**Per-meeting columns** (vary per row):

| Column | Resolution | Format |
|--------|-----------|--------|
| Building | `lookups.RoomsById[sched.RoomId]?.Building` | Single string |
| RoomNumber | `lookups.RoomsById[sched.RoomId]?.RoomNumber` | Single string |
| MeetingType | `lookups.MeetingTypesById[sched.MeetingTypeId]?.Name` | Single string |

#### Missing data handling

Any null ID or ID-not-in-dictionary produces an empty string in the
CSV field. This matches the current exporter's behavior with
`courseCodeLookup` (which returns the raw ID as fallback via
`courses.GetValueOrDefault(id, id)`). For the enriched columns, empty
is the right fallback — the importer treats missing values as
"unassigned" in that dimension.

Specific cases:
- **No instructors assigned** → Instructor and Initials columns are
  both empty strings.
- **Instructor ID not in dictionary** (deleted instructor still
  assigned to section) → that entry is skipped in the pipe-joined
  list. If all assignments are stale, both columns are empty.
- **No room assigned** (`sched.RoomId == null`) → Building and
  RoomNumber are both empty.
- **Room ID not in dictionary** (deleted room) → same as no room.
- **No campus / section type / meeting type** → empty string.
- **No tags** → empty string.
- **Tag ID not in dictionary** (deleted tag) → that tag is dropped
  from the pipe-joined list.
- **No level** → empty string.

#### Pipe delimiter and CSV escaping interaction

The pipe character `|` is used to delimit multi-value fields within
a single CSV column. Since `|` is not a CSV-special character, it
does not trigger RFC-4180 quoting on its own. However, the values
themselves may contain commas (instructor names: `"Smith, John"`)
or other CSV-special characters, so the entire field goes through
`CsvEscape()` as today.

The resulting CSV cell for an Instructor field looks like:
```
"Smith, John|Doe, Jane"
```
The outer quotes are CSV quoting (because of the commas inside the
names). The pipe is a literal within the quoted field.

The pipe character itself should never appear in instructor names,
room numbers, tag names, etc. — it has no legitimate use in those
fields. If it does appear (data entry error), the importer would
mis-split the value. This is an acceptable edge case — the same
class of problem as a comma in an unquoted CSV field. No escaping
of the pipe delimiter itself is needed.

#### Instructor name format

Instructors are exported as `"LastName, FirstName"` — the same
format used in the application's instructor management UI. The comma
between last and first name lives inside a CSV-quoted field and does
not conflict with the CSV column delimiter.

The exporter formats each instructor as:
```
$"{instructor.LastName}, {instructor.FirstName}"
```

If `FirstName` is empty (some institutions use single-name
entries), the format is `"LastName, "` — the trailing comma-space
is present but the importer's `"LastName, FirstName"` split handles
this correctly (FirstName resolves to empty string).

#### Unscheduled sections

Sections with no meetings (empty `Schedule` list) produce one row
with blank time fields (Day, StartTime, EndTime, DurationMin,
StartMinutes, Frequency) — same as today. The enriched per-section
columns (Instructor, Initials, Campus, SectionType, Tags, Level)
are still populated. The per-meeting columns (Building, RoomNumber,
MeetingType) are empty (no meeting to derive them from).

This is correct: an unscheduled section still has an instructor,
campus, and tags. Those properties should be exported so the
importer can resolve them for filtering, even though the section
has no time-slot on the grid.

#### Row structure summary

Each CSV data row has 18 fields, in this order:

```
CourseCode, SectionCode, Notes,                     ← existing (1–3)
Day, StartTime, EndTime, DurationMin,               ← existing (4–7)
StartMinutes, Frequency,                            ← existing (8–9)
Instructor, Initials,                               ← new, per-section (10–11)
Building, RoomNumber,                               ← new, per-meeting (12–13)
Campus, SectionType, Tags, MeetingType, Level       ← new, mixed scope (14–18)
```

For a section with 3 meetings (MWF), the exporter produces 3 rows.
Columns 1–3 and 10–11 and 14–16 and 18 repeat identically across
all 3 rows (per-section scope). Columns 4–9, 12–13, and 17 vary
per row (per-meeting scope).

#### Call site in SharingViewModel

`ExportSharedSchedule()` currently builds a `courseCodeLookup` and
calls `_exporter.Export(stream, sourceLabel, sections, courseCodeLookup)`.

Enhanced:
```csharp
var courseCodeLookup = ...;  // unchanged

var lookups = new ExportLookups(
    InstructorsById:  _instructorRepo.GetAll().ToDictionary(i => i.Id),
    RoomsById:        _roomRepo.GetAll().ToDictionary(r => r.Id),
    CampusesById:     _campusRepo.GetAll().ToDictionary(c => c.Id),
    SectionTypesById: _envRepo.GetAll("sectionType").ToDictionary(v => v.Id),
    TagsById:         _envRepo.GetAll("tag").ToDictionary(v => v.Id),
    MeetingTypesById: _envRepo.GetAll("meetingType").ToDictionary(v => v.Id));

var error = _exporter.Export(stream, sourceLabel, sections, courseCodeLookup, lookups);
```

The lookups are built fresh on each export. This is deliberate:
entity data may have changed since the last export (instructor
renamed, tag added), and exports should always reflect the current
state. The cost is trivial (in-memory dictionary construction from
small entity lists).

### Enriched Parser Changes

The parser currently produces `SharedSection` / `SharedMeeting`
objects. It will produce `Section` / `SectionDaySchedule` objects
instead, but the resolution step is a separate phase:

1. **Parse phase** — `SharedScheduleCsvParser.Parse()` reads CSV
   rows and produces raw `Section` objects with string metadata
   stored in the transient display fields. At this stage:
   - `Id` = `Guid.NewGuid()` (synthetic)
   - `IsShared` = `true`
   - `CourseId` = `null`
   - `DisplayCourseCode` = the exported CourseCode string
   - `SectionCode` = the exported SectionCode string
   - `DisplayInstructors` = parsed from the Instructor and Initials
     columns
   - `Schedule` entries have `RoomId = null`, `MeetingTypeId = null`
   - `CampusId = null`, `SectionTypeId = null`, `TagIds` empty,
     `Level` set directly from the CSV

2. **Resolution phase** — a new `ImportResolver.Resolve()` method
   takes the parsed sections and an `ImportResolutionIndex`, and
   populates entity IDs:
   - Instructor matching → creates `InstructorAssignment` entries
     with resolved IDs
   - Room matching → sets `SectionDaySchedule.RoomId`
   - Campus matching → sets `Section.CampusId`
   - Section type matching → sets `Section.SectionTypeId`
   - Tag matching → populates `Section.TagIds`
   - Meeting type matching → sets `SectionDaySchedule.MeetingTypeId`
   - Returns `ImportResolutionSummary` with counts and warnings

The two phases are separate classes to keep parsing logic (CSV
format concerns) decoupled from resolution logic (entity matching
concerns). Both are stateless and independently testable.

### Import Orchestration

The import flow in `SharingViewModel.ImportSharedSchedule()` becomes:

```
1. Read file (deadline-bounded, as today)
2. Parse CSV → ImportResult with List<Section> (unresolved)
3. Build ImportResolutionIndex from repositories
4. Resolve sections against index → ImportResolutionSummary
5. Stamp SemesterId on all sections (current semester)
6. Create SharedScheduleSet with resolved sections + summary
7. Add to SharedScheduleService
8. Display status message with resolution summary
```

Steps 3–4 are new. The index construction and resolution are
synchronous (in-memory dictionary lookups against local entity
lists, typically < 1ms). No async or background work needed.

### Conflict Detection Integration

#### Principle: expand the input, not the service

All three conflict services are pure functions:
`(sections, lookups) → conflicts`. They do not need to know about
shared sections as a concept. The change is at the call sites, which
merge shared sections into the input list.

#### RoomConflictService — call site changes

`SectionListViewModel` currently calls:
```csharp
foreach (var (semesterId, sections) in _sectionStore.SectionsBySemester)
{
    var semConflicts = RoomConflictService.DetectConflicts(
        sections, roomNameById, courseCodeById);
    ...
}
```

Enhanced: merge shared sections into the section list for each
semester, and extend `courseCodeById` to include shared sections:

```csharp
foreach (var (semesterId, localSections) in _sectionStore.SectionsBySemester)
{
    var sharedSections = GetSharedSectionsForSemester(semesterId);
    var allSections = localSections.Concat(sharedSections).ToList();

    // Extend courseCodeById with shared sections' display course codes
    foreach (var s in sharedSections)
        courseCodeById.TryAdd(s.Id, $"{s.DisplayCourseCode} {s.SectionCode}");

    var semConflicts = RoomConflictService.DetectConflicts(
        allSections, roomNameById, courseCodeById);
    ...
}
```

The service's `courseLabel` fallback (`section.SectionCode`) already
handles the case where `CourseId` is null, but the display is better
with the full `DisplayCourseCode` in the lookup.

After detection, conflict descriptions for shared sections are
written to `Section.RoomConflictNote` (the `[JsonIgnore]` mutable
property). Conflict descriptions for local sections are written to
`SectionListItemViewModel.RoomConflictWarning` as today.

#### InstructorConflictService — call site changes

Same pattern as room conflicts. `SectionListViewModel` merges shared
sections and extends `courseCodeById`. After detection, shared section
conflicts go to `Section.InstructorConflictNote`.

#### InstructorConflictService.DetectConflictsByInstructor — WorkloadPanelViewModel

`WorkloadPanelViewModel` calls `DetectConflictsByInstructor`. Same
merge: shared sections (for the relevant semester) are concatenated
into the input list. Since this method is keyed by instructor ID
(not section ID), shared sections with resolved instructor IDs
automatically participate. No output routing change needed — the
result is used to flag instructors, not individual sections.

#### ProgramConflictService — call site changes

`ScheduleGridViewModel.ReloadCore()` currently calls:
```csharp
var visibleSections = lookups.Sections
    .Where(s => visibleSectionIds.Contains(s.Id)).ToList();
var tagIdsBySectionId = visibleSections
    .ToDictionary(s => s.Id, s => (IReadOnlyList<string>)s.TagIds);
programConflicts = ProgramConflictService.DetectConflicts(
    enabledWatches, visibleSections, tagIdsBySectionId);
```

Enhanced: include shared sections (for the current semester) in
`visibleSections` and `tagIdsBySectionId`. Shared sections that
passed the grid filter are already in the visible set (per Phase 3's
filter integration). The `tagIdsBySectionId` dictionary simply
includes their entries.

The service groups sections by `CourseId` and compares across groups.
Shared sections have `CourseId = null`, which maps to `string.Empty`
in the grouping. This means all shared sections land in one group
and are compared against every local course group — correct behavior,
since a shared section from department B is by definition a different
course than anything in A's catalog.

Course-mode watches exclude shared sections naturally: they match on
`CourseId`, and shared sections have `CourseId = null`, so
`courseIdSet.Contains(null)` is false. No special-casing needed.

### Helper: GetSharedSectionsForSemester

A convenience method on `SharedScheduleService`:

```csharp
public IReadOnlyList<Section> GetSectionsForSemester(string semesterId)
```

Returns all sections across all loaded sets whose `SemesterId`
matches. Used by the conflict detection call sites and the grid
pipeline to merge shared sections into the local section list.

### Data Flow Summary

```
EXPORT PATH
───────────
SharingViewModel
  ├── ISectionRepository.GetAll()      → sections to export
  ├── ICourseRepository.GetAll()       → courseCodeLookup
  ├── IInstructorRepository.GetAll()   ─┐
  ├── IRoomRepository.GetAll()          │
  ├── ICampusRepository.GetAll()        ├→ ExportLookups
  ├── ISchedulingEnvironmentRepository  │
  │   .GetAll("sectionType")            │
  │   .GetAll("tag")                    │
  │   .GetAll("meetingType")           ─┘
  └── SharedScheduleCsvExporter.Export(sections, courseCodeLookup, lookups)
       └── writes enriched 18-column CSV

IMPORT PATH
───────────
SharingViewModel
  ├── SharedScheduleCsvParser.Parse(stream)
  │    └── returns ImportResult with List<Section> (unresolved)
  ├── ImportResolutionIndex.Build(instructors, rooms, campuses, ...)
  │    └── built from same repositories as export
  ├── ImportResolver.Resolve(sections, index)
  │    ├── populates entity IDs on Section objects
  │    └── returns ImportResolutionSummary
  └── SharedScheduleService.Add(set)
       └── notifies grid VM → reload triggers conflict detection

CONFLICT DETECTION (unchanged services, expanded inputs)
─────────────────────────────────────────────────────────
SectionListViewModel
  ├── _sectionStore.SectionsBySemester (local)
  ├── _sharedScheduleService.GetSectionsForSemester(...) (shared)
  ├── merge → allSections
  ├── RoomConflictService.DetectConflicts(allSections, ...)
  │    └── conflicts on shared sections → Section.RoomConflictNote
  └── InstructorConflictService.DetectConflicts(allSections, ...)
       └── conflicts on shared sections → Section.InstructorConflictNote

ScheduleGridViewModel
  ├── visibleSections (local + shared, post-filter)
  └── ProgramConflictService.DetectConflicts(watches, visibleSections, tags)
       └── output feeds AccessPanelViewModel + grid teal glow

WorkloadPanelViewModel
  ├── _sectionStore.SectionsBySemester (local)
  ├── _sharedScheduleService.GetSectionsForSemester(...) (shared)
  ├── merge → allSections
  └── InstructorConflictService.DetectConflictsByInstructor(allSections, ...)
```

### DI Registration Changes

`SharingViewModel` gains four new constructor dependencies:

```
IInstructorRepository
IRoomRepository
ICampusRepository
ISchedulingEnvironmentRepository
```

These are already registered in the DI container (used by other view
models). No new registrations needed — just additional constructor
parameters on `SharingViewModel`.

### What Phase 4 Does NOT Specify

- **View-layer details** — how section list cards render purple text,
  how the strip formats resolution stats, how tooltips display
  conflict notes. These are Phase 5 (Architecture) concerns.
- **Grid pipeline integration** — how shared sections enter
  `BuildFilteredBlocks`, how `ToEntry` handles `IsShared` sections,
  how `ComputeTiles` interleaves them. Phase 5.
- **Old model retirement** — the mechanics of removing
  `SharedSection`, `SharedMeeting`, `SharedScheduleBlock`, and
  `BuildSharedScheduleBlocks`. Phase 5.

### Open Questions for Phase 5
1. **Shared section list ordering** — Phase 3 says shared sections
   appear after local sections, sub-grouped by source. Where does
   the partition logic live: in `SectionListViewModel`'s sort
   comparator, or in the `Items` collection construction?
2. **Grid pipeline restructuring** — shared sections currently enter
   after `BuildFilteredBlocks` via a separate `BuildSharedScheduleBlocks`
   pass. The enhanced design puts them into the section list before
   filtering. How is `lookups.Sections` augmented? Does
   `BuildLookups` merge shared sections, or does `ReloadCore` merge
   them after `BuildLookups` returns?
3. **Workload panel shared chips** — how does
   `WorkloadPanelViewModel.BuildItemsForInstructor` distinguish
   shared section chips (purple, non-clickable, with source label)
   from local ones? Does it check `IsShared` on the `Section` or
   receive a pre-partitioned input?
4. **Conflict note routing** — how do conflict descriptions flow
   from `DetectConflicts` output to `Section.RoomConflictNote` /
   `InstructorConflictNote`? Does the call site in
   `SectionListViewModel` write them directly, or does a
   post-processing step distribute them?

---

## Phase 5 — Architecture
**Status: LOCKED** — signed off 2026-07-15

This phase specifies how the data design from Phase 4 maps onto the
existing ViewModel/Service/View layers. It covers: resolved open
questions, new classes, modified classes, view changes, model
retirement, and DI registration.

### Resolved: Open Questions from Phase 4

**Q1 — Shared section list ordering.** The partition lives in
`SectionListViewModel.LoadCore`, not in the sort comparator.
`LoadCore` already loops per-semester and builds a list of
`SectionListItemViewModel` instances per semester. After sorting
local items via `SortItems()`, it appends shared items (from
`SharedScheduleService.GetSectionsForSemester`) in a second pass,
sub-grouped by source label and sorted internally by
`(CourseCode, SectionCode)`. No change to `SortItems` itself —
the partition is structural, not a sort key.

**Q2 — Grid pipeline restructuring.** `BuildLookups` is unchanged —
it still builds `GridLookups` from A's local data only. This is
critical: `GridLookups.Sections` feeds `PopulateFilterOptions`, and
shared sections must never pollute filter drop-downs (C2).

Instead, `ReloadCore` merges shared sections into the section list
*after* `BuildLookups` returns but *before* `BuildFilteredBlocks`
runs. The merge point is a new local variable `allSections` that
concatenates `lookups.Sections` with
`_sharedScheduleService.GetSectionsForSemester(semId)`. This
combined list is passed to `BuildFilteredBlocks`. The separate
`BuildSharedScheduleBlocks` pass is removed entirely.

`BuildFilteredBlocks` gains an `IReadOnlyDictionary<string, Course>`
parameter (already available from `lookups.Courses`) to support
`BuildSectionLabel` for shared sections that have no `CourseId`.
For `IsShared` sections, the label comes from `DisplayCourseCode`
and initials from `DisplayInstructors` — no course/instructor
dictionary lookup needed.

**Q3 — Workload panel shared chips.** `BuildItemsForInstructor`
checks `section.IsShared` on the `Section` object directly. When
true, it creates a `WorkloadItemViewModel` with
`Kind = WorkloadItemKind.SharedSection` (new enum value) and appends
the source label to the display string. The view uses `IsShared`
(new computed property: `Kind == WorkloadItemKind.SharedSection`)
for purple text and click suppression. No pre-partitioned input —
the method receives one merged section list and handles both kinds
inline.

**Q4 — Conflict note routing.** The call sites in
`SectionListViewModel` (`ApplyRoomConflicts`, `ApplyInstructorConflicts`)
write directly to the shared `Section` objects' mutable properties.
After `DetectConflicts` returns, the call site loops the result:
- For local sections: writes to
  `SectionListItemViewModel.RoomConflictWarning` /
  `InstructorConflictWarning` (as today).
- For shared sections: writes to `Section.RoomConflictNote` /
  `Section.InstructorConflictNote` (the `[JsonIgnore]` mutable
  properties). These are then read by the shared section's
  `SectionListItemViewModel` and by the grid tile tooltip.

No post-processing step — the same loop handles both, branching on
`section.IsShared`.

### New Classes

#### `ImportResolutionIndex`

**File**: `Services/ImportResolutionIndex.cs`

Immutable lookup structure. Built once per import from A's local
entity repositories. Specified in Phase 4 — this section covers
the class shape and API.

```csharp
public class ImportResolutionIndex
{
    // All dictionaries use OrdinalIgnoreCase comparison
    // (custom IEqualityComparer for tuple keys)

    public static ImportResolutionIndex Build(
        IReadOnlyList<Instructor> instructors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<Campus> campuses,
        IReadOnlyList<SchedulingEnvironmentValue> sectionTypes,
        IReadOnlyList<SchedulingEnvironmentValue> tags,
        IReadOnlyList<SchedulingEnvironmentValue> meetingTypes)

    // Lookup methods — return null/empty when no match
    public Instructor? ResolveInstructor(
        string lastName, string firstName, string initials)
    public Room? ResolveRoom(
        string building, string roomNumber)
    public Campus? ResolveCampus(string name)
    public SchedulingEnvironmentValue? ResolveSectionType(string name)
    public SchedulingEnvironmentValue? ResolveTag(string name)
    public SchedulingEnvironmentValue? ResolveMeetingType(string name)
}
```

The `Build` factory constructs all internal dictionaries from plain
entity lists — no DI dependency. Each `Resolve*` method normalizes
its input (trim, collapse whitespace) and performs the lookup
described in Phase 2.

`ResolveInstructor` implements the three-tier match: single match →
resolve; multiple matches → initials tiebreaker; still ambiguous →
null + warning (appended to a `List<string>` passed to each resolve
call, or returned via the summary).

Design note: `Resolve*` methods are intentionally individual lookups
rather than a batch "resolve all sections" method. This keeps the
index a pure lookup structure and leaves orchestration to
`ImportResolver`.

#### `ImportResolver`

**File**: `Services/ImportResolver.cs`

Stateless service that maps parsed (unresolved) `Section` objects
to resolved `Section` objects using an `ImportResolutionIndex`.

```csharp
public class ImportResolver
{
    public ImportResolutionSummary Resolve(
        IReadOnlyList<Section> sections,
        ImportResolutionIndex index)
}
```

`Resolve` mutates the `Section` objects in place (they are freshly
constructed by the parser, not shared with any other consumer).
For each section:

1. **Instructors**: parse each `DisplayInstructors` entry's
   `(Name, Initials)`. Split `Name` on `", "` to get
   `(LastName, FirstName)`. Call `index.ResolveInstructor(...)`.
   If resolved, create an `InstructorAssignment` with the
   matched instructor's ID and add it to
   `section.InstructorAssignments`.

2. **Rooms**: for each `SectionDaySchedule` in `section.Schedule`,
   use the parsed building/room-number strings (stored temporarily
   on the schedule entry — see Parser Changes below). Call
   `index.ResolveRoom(building, roomNumber)`. If resolved, set
   `sched.RoomId`.

3. **Campus, SectionType, Tags, MeetingType**: straightforward
   single-value or multi-value lookups, setting the corresponding
   ID properties on the section or schedule entry.

4. **Level**: no resolution needed — already set by the parser.

Returns an `ImportResolutionSummary` with distinct-name counts and
any warnings.

`ImportResolver` is registered as a singleton in DI (stateless,
no instance state). It could also be a static class, but a
singleton keeps the option open for future dependency injection
(e.g. logging) without changing call sites.

#### `ExportLookups`

**File**: `Services/ExportLookups.cs` (or nested in the exporter file)

Lightweight record specified in Phase 4:

```csharp
public record ExportLookups(
    IReadOnlyDictionary<string, Instructor> InstructorsById,
    IReadOnlyDictionary<string, Room> RoomsById,
    IReadOnlyDictionary<string, Campus> CampusesById,
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> SectionTypesById,
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> TagsById,
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> MeetingTypesById);
```

Constructed by `SharingViewModel` from repository `GetAll()` calls,
passed to the exporter. No DI registration needed — it's a data
carrier.

#### `ImportResolutionSummary`

**File**: `Models/ImportResolutionSummary.cs`

Record specified in Phase 4. No behavior — pure data.

### Modified Classes

#### `Section` (model)

**File**: `Models/Section.cs`

Add six `[JsonIgnore]` properties as specified in Phase 4:

```csharp
[JsonIgnore] public bool IsShared { get; init; }
[JsonIgnore] public string? SourceLabel { get; init; }
[JsonIgnore] public string? DisplayCourseCode { get; init; }
[JsonIgnore] public List<(string Name, string Initials)>?
             DisplayInstructors { get; init; }
[JsonIgnore] public string? RoomConflictNote { get; set; }
[JsonIgnore] public string? InstructorConflictNote { get; set; }
```

Zero cost for local sections — all default to `false`/`null`,
never serialized, no allocation.

#### `SectionDaySchedule` (model)

**File**: `Models/SectionDaySchedule.cs` (or wherever schedule
entries are defined)

Add two `[JsonIgnore]` transient string properties for the parser
to stash raw building/room-number strings before resolution:

```csharp
[JsonIgnore] public string? ImportedBuilding { get; init; }
[JsonIgnore] public string? ImportedRoomNumber { get; init; }
```

Also add one for meeting type name:

```csharp
[JsonIgnore] public string? ImportedMeetingTypeName { get; init; }
```

These are consumed by `ImportResolver.Resolve()` and discarded
after resolution (the resolved IDs are set on `RoomId` /
`MeetingTypeId`). Never persisted, never visible outside the
import pipeline.

#### `SharedScheduleService`

**File**: `Services/SharedScheduleService.cs`

**Changes:**
- `Sets` property type changes: `SharedScheduleSet.Sections` is now
  `List<Section>` (per Phase 4), not `List<SharedSection>`.
- **Remove** `BuildBlocks()` method. The grid no longer needs it —
  shared sections flow through `BuildFilteredBlocks` as regular
  `Section` objects, producing `SectionMeetingBlock` entries.
- **Add** `GetSectionsForSemester(string semesterId)`:
  ```csharp
  public IReadOnlyList<Section> GetSectionsForSemester(
      string semesterId)
  ```
  Returns all sections across all loaded sets whose `SemesterId`
  matches. Flat list — source grouping is handled by callers that
  need it (strip VM, section list VM).

The `Add`, `Dismiss`, `DismissAll`, `Changed` event, and `HasAny`
property are unchanged.

#### `SharedScheduleCsvParser`

**File**: `Services/SharedScheduleCsvParser.cs`

**Parse method** — returns `Section` objects instead of
`SharedSection` objects:

```csharp
public ImportResult Parse(Stream stream, string fallbackSourceLabel)
```

`ImportResult` changes:
```csharp
public record ImportResult(
    SharedScheduleSet? Set,    // Set.Sections is now List<Section>
    int TotalRows,
    int SkippedRows,
    List<(int LineNumber, string Reason)> Warnings,
    string? FileError,
    string? SemesterName)      // NEW: from CSV header, for mismatch warning
```

**Parsing changes:**
- Read the new header comment format:
  `#TermPoint Schedule Overlay,{source},{semester},{date}`.
  Extract `SemesterName` (3rd field) and `ExportedAt` (4th field).
  Old-format headers (2 fields) have `SemesterName = null`.
- Column dictionary: recognize columns 10–18 by header name.
  Missing columns (old CSV) → those fields stay null/empty.
- Group rows by `(CourseCode, SectionCode)` as today, but build
  `Section` objects:
  - `Id = Guid.NewGuid().ToString()` (synthetic)
  - `IsShared = true`
  - `CourseId = null`
  - `DisplayCourseCode = courseCode`
  - `SectionCode = sectionCode`
  - `Notes = notes`
  - `Level = level` (column 18, or empty)
  - `DisplayInstructors` parsed from Instructor + Initials columns
    (pipe-split, zip by position)
  - `Schedule` entries: `SectionDaySchedule` with day, start, end,
    duration, frequency, plus `ImportedBuilding`,
    `ImportedRoomNumber`, `ImportedMeetingTypeName` from columns
    12/13/17
  - `CampusId = null`, `SectionTypeId = null`, `TagIds = []`
    (resolved later)

  Per-section fields (Instructor, Initials, Campus, SectionType,
  Tags, Level) are read from the first row of each group and
  ignored on subsequent rows. Per-meeting fields (Building,
  RoomNumber, MeetingType) are read per row.

- Stash raw imported strings for campus, section type, and tags on
  the section for the resolver to consume. Options:
  - (a) Additional `[JsonIgnore]` properties on `Section`:
    `ImportedCampusName`, `ImportedSectionTypeName`,
    `ImportedTagNames`.
  - (b) Pass them alongside in a separate structure.

  Option (a) is simpler and consistent with the
  `SectionDaySchedule.ImportedBuilding` pattern. Three more
  `[JsonIgnore] init` properties on `Section`:

  ```csharp
  [JsonIgnore] public string? ImportedCampusName { get; init; }
  [JsonIgnore] public string? ImportedSectionTypeName { get; init; }
  [JsonIgnore] public List<string>? ImportedTagNames { get; init; }
  ```

#### `SharedScheduleCsvExporter`

**File**: `Services/SharedScheduleCsvExporter.cs`

**Export method** — gains `ExportLookups` and semester name:

```csharp
public string? Export(
    Stream output,
    string sourceLabel,
    string semesterName,
    IReadOnlyList<Section> sections,
    Func<string, string> courseCodeLookup,
    ExportLookups lookups)
```

**Changes:**
- Header comment: `#TermPoint Schedule Overlay,{source},{semester},{date}`
  (adds semester name as 3rd field).
- Header row: 18 columns instead of 9.
- Per row: resolve IDs to display strings using `lookups`, write
  all 18 fields. Per-section fields repeat; per-meeting fields vary.
  Resolution logic specified in Phase 4 §Enriched Exporter.
- Missing data → empty string (specified in Phase 4 §Missing data
  handling).

#### `SharingViewModel`

**File**: `ViewModels/Management/SharingViewModel.cs`

**New constructor dependencies** (4 additional):
```
IInstructorRepository
IRoomRepository
ICampusRepository
ISchedulingEnvironmentRepository
```

These are already registered in DI. `SharingViewModel` is transient,
so no singleton-ordering concerns.

**ImportSharedSchedule changes:**
After `_parser.Parse(stream, fallbackLabel)` returns:

```
1. Parse → ImportResult with unresolved List<Section> + SemesterName
2. Check SemesterName vs active semester → append warning if mismatch
3. Build ImportResolutionIndex from 6 repo GetAll() calls
4. Call _resolver.Resolve(sections, index) → ImportResolutionSummary
5. Stamp SemesterId on all sections (active semester ID)
6. Create SharedScheduleSet with resolved sections + summary
7. _sharedScheduleService.Add(set)
8. Build StatusMessage from parse stats + resolution summary
```

Step 5 is new vs today: sections get the active semester's ID so
they participate in semester-scoped operations. Today,
`SharedScheduleSet` has no semester concept — blocks were stamped
with the semester in `BuildBlocks()`. Now sections carry it
directly.

**ExportSharedSchedule changes:**
Build `ExportLookups` from repo calls (as specified in Phase 4
§Call site in SharingViewModel). Pass it plus the active semester
name to the exporter.

**Status message enhancement:**
After import, build a multi-line status message:
- Line 1: `"Imported {n} sections from {source}."`
- Line 2: `"Matched: {x} instructors, {y} rooms, {z} tags."`
- Line 3 (if warnings): individual warning lines.
- Semester mismatch warning (if applicable).

The `StatusMessage` property is already a `string` bound in the
sharing flyout. Multi-line strings render naturally in the
TextBlock.

#### `ScheduleGridViewModel`

**File**: `ViewModels/GridView/ScheduleGridViewModel.cs`

This is the largest change. The grid pipeline drops Pass 4
(shared blocks) and integrates shared sections into Pass 1
(filtered blocks).

**Constructor:** Add `SharedScheduleService` injection (already
present — it's used for `BuildSharedScheduleBlocks` today). No
new dependencies.

**ReloadCore changes:**

Today's pipeline:
```
Pass 1: BuildFilteredBlocks(lookups, snap)     → filtered
Pass 2: BuildOverlayBlocks(lookups, snap, ...)  → overlayOnly
Pass 3: BuildCommitmentBlocks(...)              → commitments
Pass 4: BuildSharedScheduleBlocks()             → sharedBlocks
combinedBlocks = filtered ∪ overlay ∪ commitments ∪ meetings ∪ shared
```

Enhanced pipeline:
```
Merge:  allSections = lookups.Sections ++ sharedSections
Pass 1: BuildFilteredBlocks(allSections, lookups, snap) → filtered
Pass 2: BuildOverlayBlocks(lookups, snap, ...)          → overlayOnly
Pass 3: BuildCommitmentBlocks(...)                      → commitments
combinedBlocks = filtered ∪ overlay ∪ commitments ∪ meetings
```

Pass 4 is gone. Shared sections produce `SectionMeetingBlock`
entries in Pass 1, just like local sections.

**Merge point** (new code in `ReloadCore`, before Pass 1):

```csharp
// Merge shared sections into the section list for filtering.
// lookups.Sections stays unchanged (local only) to preserve
// filter option population (C2).
var sharedSections = _sharedScheduleService
    .GetSectionsForSemester(semId);
var allSections = lookups.Sections
    .Concat(sharedSections).ToList();
```

`allSections` is passed to `BuildFilteredBlocks`. `lookups` itself
is not mutated — `PopulateFilterOptions` continues to read
`lookups.Sections` (local only), preserving C2.

**BuildFilteredBlocks changes:**

Signature gains a section list parameter (decoupled from lookups):
```csharp
internal static List<SectionMeetingBlock> BuildFilteredBlocks(
    IReadOnlyList<Section> sections,    // ← was lookups.Sections
    GridLookups lookups,
    FilterSnapshot snap,
    ...)
```

Filter predicate changes for shared sections:

- **Course filter** (C4 exemption): skip `IsShared` sections.
  ```csharp
  if (snap.FilterCourse
      && !section.IsShared    // ← NEW: shared sections pass
      && !snap.CourseIds.Contains(section.CourseId ?? ""))
      continue;
  ```

- **Subject filter** (C4 exemption): skip `IsShared` sections.
  ```csharp
  if (snap.FilterSubject && !section.IsShared)  // ← NEW
  {
      // existing subject lookup logic
  }
  ```

- **All other filters** (instructor, room, campus, section type,
  tags, meeting type, level): apply identically. Shared sections
  carry resolved IDs, so the existing predicates evaluate them
  naturally. No code change needed in these predicates.

- **Unstaffed sentinel**: shared sections with no resolved
  instructors are "unstaffed" in A's world. The existing
  `NotStaffedSelected` check (`section.InstructorIds.Count == 0`)
  naturally includes them. This is correct — it lets the user see
  "all unstaffed sections, including shared ones with no local
  instructor match."

**BuildSectionLabel changes:**

The method must handle shared sections that have no `CourseId` and
may have unresolved instructors:

```csharp
internal static (string Label, string Initials) BuildSectionLabel(
    Section section,
    IReadOnlyDictionary<string, Course> courses,
    IReadOnlyDictionary<string, Instructor> instructors)
{
    string label;
    if (section.IsShared)
    {
        // Use DisplayCourseCode directly — no course lookup
        label = $"{section.DisplayCourseCode} {section.SectionCode}";
    }
    else
    {
        // Existing logic: look up CourseId → CalendarCode
        ...
    }

    string initials;
    if (section.IsShared && section.DisplayInstructors is { } di)
    {
        // For resolved instructors, use A's Initials field.
        // For unresolved, use the CSV-provided initials.
        var parts = new List<string>();
        int assignmentIdx = 0;
        foreach (var (name, csvInitials) in di)
        {
            if (assignmentIdx < section.InstructorAssignments.Count)
            {
                var assignment = section.InstructorAssignments[assignmentIdx];
                if (instructors.TryGetValue(
                        assignment.InstructorId, out var inst))
                    parts.Add(inst.Initials);
                else
                    parts.Add(csvInitials);
                assignmentIdx++;
            }
            else
            {
                parts.Add(csvInitials);
            }
        }
        initials = string.Join(" ", parts);
    }
    else
    {
        // Existing logic: look up InstructorIds → Initials
        ...
    }

    return (label, initials);
}
```

Actually, this conflates resolved and unresolved instructor order.
Simpler approach: `DisplayInstructors` is the authoritative
ordered list for shared sections. For each entry, check if a
corresponding resolved `InstructorAssignment` exists (by matching
the assignment's instructor ID back to the `DisplayInstructors`
index). But this is fragile.

Better: `DisplayInstructors` carries initials for *all* instructors,
and those initials are already the right display value (they come
from the exporting department's data). For resolved instructors,
A's own initials should be used (they might differ from B's — e.g.,
a cross-appointed instructor whose initials are set differently in
each department). However, in practice, departments don't rename
cross-appointed instructors' initials. The simpler design:

**For shared sections, always use `DisplayInstructors` initials
for grid tile rendering.** This avoids the resolved-vs-unresolved
bookkeeping entirely. The resolved instructor IDs are used for
*filtering* and *conflict detection* (where correctness matters),
not for display initials (where a cosmetic difference is harmless).

```csharp
if (section.IsShared)
{
    label = string.IsNullOrEmpty(section.DisplayCourseCode)
        ? section.SectionCode
        : $"{section.DisplayCourseCode} {section.SectionCode}";

    initials = section.DisplayInstructors is { Count: > 0 } di
        ? string.Join(" ", di.Select(d => d.Initials))
        : "";
}
```

**SectionMeetingBlock changes:**

Add `IsSharedSchedule` flag to the record:

```csharp
public record SectionMeetingBlock(
    ...,
    bool IsSharedSchedule = false    // ← NEW
) : GridBlock(...);
```

Set in `BuildFilteredBlocks` when creating blocks for shared
sections:

```csharp
new SectionMeetingBlock(
    ...,
    IsSharedSchedule: section.IsShared
)
```

**ToEntry changes:**

Remove the `SharedScheduleBlock` case entirely. The
`SectionMeetingBlock` case now propagates `IsSharedSchedule`:

```csharp
SectionMeetingBlock s => new TileEntry(
    s.Label, s.Initials, s.SectionId,
    s.IsOverlay, IsCommitment: false,
    s.FrequencyAnnotation, s.IsDeemphasized,
    IsEmphasized: s.IsEmphasized,
    IsSharedSchedule: s.IsSharedSchedule,  // ← NEW
    Flag: s.Flag),
```

Key change from today: shared schedule entries now have
`IsCommitment = false` and carry a real `SectionId` (the synthetic
GUID). This means:
- They are **selectable** (the renderer's click handler checks
  `IsCommitment`, and false means clicks are allowed).
- They have a **cursor** (hand cursor on hover).
- They are registered for **selection repainting**.
- The selection sets `SectionId` on `SectionStore`, which
  highlights the corresponding card in the section list.

The purple text rendering is unchanged — the renderer already
checks `IsSharedSchedule` for the `SharedScheduleText` brush.

**Context menu suppression**: the grid's right-click handler must
check `IsSharedSchedule` and suppress the context menu (no edit
operations on shared sections). Add:
```csharp
if (entry.IsSharedSchedule) { e.Handled = true; return; }
```

**DeduplicateBlocks changes:**

Remove the `SharedScheduleBlock` case from the `EntityId` helper.
Shared sections are now `SectionMeetingBlock` entries with real
(synthetic) `SectionId`s, so the existing `SectionMeetingBlock`
case handles them:
```csharp
SectionMeetingBlock s => s.SectionId,
```

No collision risk — synthetic GUIDs don't collide with A's real
section IDs.

**ProgramConflictService call site:**

The `visibleSections` list is already built from the filtered
blocks' `SectionId` values looked up in the section list. After
the merge, shared sections are in the section list that
`BuildFilteredBlocks` processes, so they're automatically in the
filtered blocks, and thus in `visibleSections`. The
`tagIdsBySectionId` dictionary includes shared sections' resolved
tag IDs from `section.TagIds`. No explicit change needed — the
merge upstream handles it.

**Tooltip building:**

Remove `BuildSharedScheduleTooltip`. Shared entries are now
`SectionMeetingBlock` with real `SectionId`, so they flow through
the existing `BuildTileTooltip` path. For shared sections,
`BuildTileTooltip` should include the source label. The tooltip
can check `IsSharedSchedule` on the `TileEntry` and, if true,
look up the section's `SourceLabel` from a new
`sourceLabelsById` dictionary built during the merge step.

Alternatively, since tooltips for section tiles already show
course code + section code + time, and shared tiles render in
purple (immediately distinguishing them), a tooltip showing just
the standard info plus "(Chemistry Department)" is sufficient.
The `SourceLabel` can be encoded into the `TileEntry` via a new
optional property or looked up from the shared section's
`SourceLabel` field.

Simplest approach: add `SourceLabel` to `TileEntry` (default
`""`). Set it from `section.SourceLabel` for shared entries.
The tooltip builder appends it when non-empty.

**Remove:**
- `BuildSharedScheduleBlocks` method
- `BuildSharedScheduleTooltip` method
- `SharedScheduleBlock` arm in `ToEntry`
- `SharedScheduleBlock` arm in `DeduplicateBlocks`

#### `SectionListViewModel`

**File**: `ViewModels/Management/SectionListViewModel.cs`

**New dependency:** `SharedScheduleService` (injected via
constructor; already a singleton).

**LoadCore changes:**

After building and sorting local section items (existing flow),
append shared sections:

```csharp
// After sorting local items for this semester:
var sharedSections = _sharedScheduleService
    .GetSectionsForSemester(semId);

// Sub-group by source, sort within each group
var sharedBySource = sharedSections
    .GroupBy(s => s.SourceLabel ?? "")
    .OrderBy(g => g.Key);

foreach (var group in sharedBySource)
{
    foreach (var section in group
        .OrderBy(s => s.DisplayCourseCode)
        .ThenBy(s => s.SectionCode))
    {
        var item = CreateSharedSectionItem(section, lk, semName, semColor);
        rawItems.Add(item);
    }
}
```

`CreateSharedSectionItem` is a new private method that creates a
`SectionListItemViewModel` with `IsShared = true`. The lookups
are the same as for local sections — shared sections carry
resolved IDs, so the constructor populates instructor names, room
names, etc. from A's lookup dictionaries. Unresolved properties
produce empty display strings, which is correct.

**Conflict detection changes (ApplyRoomConflicts,
ApplyInstructorConflicts):**

Merge shared sections into the input list before calling
`DetectConflicts`, as specified in Phase 4 §Conflict Detection
Integration:

```csharp
var sharedSections = _sharedScheduleService
    .GetSectionsForSemester(semesterId);
var allSections = sections.Concat(sharedSections).ToList();

// Extend courseCodeById for shared sections
foreach (var s in sharedSections)
    courseCodeById.TryAdd(s.Id,
        $"{s.DisplayCourseCode} {s.SectionCode}");

var conflicts = RoomConflictService.DetectConflicts(
    allSections, roomNameById, courseCodeById);
```

After detection, route conflict descriptions:

```csharp
foreach (var (sectionId, warning) in conflicts)
{
    if (itemsById.TryGetValue(sectionId, out var vm))
    {
        // Local section — write to VM property (existing)
        vm.RoomConflictWarning = warning;
    }
    else
    {
        // Shared section — write to Section model property
        var sharedSection = sharedSections
            .FirstOrDefault(s => s.Id == sectionId);
        if (sharedSection != null)
            sharedSection.RoomConflictNote = warning;
    }
}
```

Same pattern for instructor conflicts.

**Changed event wiring:** Subscribe to
`_sharedScheduleService.Changed` to trigger a reload when shared
schedules are added/dismissed. (The grid VM already does this;
the section list VM needs to add it.)

#### `SectionListItemViewModel`

**File**: `ViewModels/Management/SectionListItemViewModel.cs`

**New properties:**

```csharp
public bool IsShared { get; init; }
```

Set to `true` when created for a shared section. Controls:
- **Expansion suppression**: `ToggleCollapsed` early-returns when
  `IsShared` is true. The card stays in summary-only mode.
- **Context menu suppression**: the view binds `ContextMenu`
  visibility to `!IsShared`.
- **Flag suppression**: flag operations check `IsShared` and
  refuse (shared sections can't be flagged).
- **Conflict display**: `RoomConflictWarning` and
  `InstructorConflictWarning` are populated from
  `section.RoomConflictNote` / `section.InstructorConflictNote`
  when `IsShared` is true, giving shared cards the same warning
  rendering as local cards.

**Source label display:**

Add `SourceLabel` property (from the section's `SourceLabel`).
The view shows it as a subtle badge or parenthetical on shared
cards: `"(Chemistry)"`.

**Constructor changes:**

The existing constructor works for shared sections — it takes a
`Section` and lookup dictionaries. For shared sections:
- `Heading` uses `DisplayCourseCode + SectionCode` (existing
  `CalendarCode` lookup returns null for null `CourseId`, and the
  fallback is `SectionCode` alone — but we want the full
  `DisplayCourseCode`). Adjust the heading logic:
  ```csharp
  if (section.IsShared && section.DisplayCourseCode != null)
      Heading = $"{section.DisplayCourseCode} {section.SectionCode}";
  ```
- `InstructorLine` is built from `InstructorAssignments` → lookup.
  For unresolved instructors, the lookup returns null, so they're
  skipped. To show all instructors (resolved + unresolved), use
  `DisplayInstructors` for shared sections:
  ```csharp
  if (section.IsShared && section.DisplayInstructors != null)
      InstructorLine = string.Join(", ",
          section.DisplayInstructors.Select(d => d.Name));
  ```

#### `WorkloadPanelViewModel`

**File**: `ViewModels/WorkloadPanelViewModel.cs`

**New dependency:** `SharedScheduleService` (injected via
constructor).

**Load changes:**

In `Load()`, after getting local sections from
`_sectionStore.SectionsBySemester`, merge shared sections:

```csharp
var sharedSections = _sharedScheduleService
    .GetSectionsForSemester(semId);
var allSections = localSections.Concat(sharedSections).ToList();
```

Pass `allSections` to `BuildItemsForInstructor` instead of
`localSections`.

**BuildItemsForInstructor changes:**

For shared sections, create `WorkloadItemViewModel` with
`Kind = WorkloadItemKind.SharedSection`:

```csharp
if (section.IsShared)
{
    var label = $"{section.DisplayCourseCode} {section.SectionCode}"
        + $" ({section.SourceLabel})";
    items.Add(new WorkloadItemViewModel
    {
        Kind = WorkloadItemKind.SharedSection,
        Id = section.Id,
        Label = label,
        WorkloadValue = 0m,  // doesn't contribute to totals
    });
}
```

Shared section chips don't contribute to `totalWorkload` or
`totalSections` counts — the summing logic skips
`Kind == SharedSection` entries.

**Instructor conflict detection:**

Merge shared sections into the input for
`InstructorConflictService.DetectConflictsByInstructor`, same
pattern as `SectionListViewModel`.

**Changed event wiring:** Subscribe to
`_sharedScheduleService.Changed` to trigger a reload.

#### `WorkloadItemViewModel`

**File**: `ViewModels/WorkloadItemViewModel.cs`

**Enum change:**
```csharp
public enum WorkloadItemKind { Section, Release, SharedSection }
```

**New computed property:**
```csharp
public bool IsShared => Kind == WorkloadItemKind.SharedSection;
```

The view uses `IsShared` for:
- Purple text (bind `Foreground` to a converter or trigger)
- Click suppression (no selection, no navigation)
- Source label suffix is already baked into `Label`

#### `SharedScheduleStripViewModel`

**File**: `ViewModels/GridView/SharedScheduleStripViewModel.cs`

**Changes to `Refresh()`:**

`SharedScheduleSet.Sections` is now `List<Section>` instead of
`List<SharedSection>`. The strip builds its rows from these.

**CollapsedSummary enhancement:**

Add resolution stats per source:
```csharp
var summary = set.ResolutionSummary;
var matchInfo = FormatResolutionBrief(summary);
// e.g. "3/4 instr" or "all matched"
```

Format: `"Chemistry Dept (12, 3/4 instr) · Biology (8, all matched)"`

**SharedScheduleSourceRow changes:**

Rows are built from `Section` objects instead of `SharedSection`.
The row's `Label` comes from `section.DisplayCourseCode + " " +
section.SectionCode`. The `Schedule` string is built from
`section.Schedule` (`SectionDaySchedule` entries) instead of
`SharedMeeting` objects. The formatting logic (group by
time/frequency, day abbreviation) is the same.

**Enhanced expanded view:**

Each source group header shows resolution stats:
```
Chemistry Department — exported 2026-07-15
Matched: 3/4 instructors · 8/8 rooms · 4/6 tags
```

Section rows show instructor initials and room shorthand:
```
CHEM101 A  JRS JD  MWF 8:00–8:50 AM  Rm 204 Sci
```

Instructor initials come from `section.DisplayInstructors`.
Room shorthand comes from the `SectionDaySchedule.RoomId` →
lookup (or `ImportedRoomNumber` / `ImportedBuilding` if
unresolved). The strip VM needs a room lookup dictionary —
it can receive this from the grid reload or build its own
lightweight one from `IRoomRepository.GetAll()`. Since the
strip is small and reloads infrequently, building its own
is simpler (no coupling to the grid pipeline).

New constructor dependency: `IRoomRepository` (for room name
lookup in expanded rows).

#### `GridFilterViewModel`

**File**: `ViewModels/GridView/GridFilterViewModel.cs`

**No changes.** Filter options are populated from `GridLookups`,
which is built from local data only. Shared sections never
contribute to filter option lists. The filter predicates in
`BuildFilteredBlocks` are where the C4 exemption is implemented
(see `ScheduleGridViewModel` changes above).

### View Layer Changes

#### `ScheduleGridView.axaml.cs`

**Text color**: No change — the existing
`entry.IsSharedSchedule ? SharedScheduleText` branch in the
foreground priority chain already handles shared entries. Now it
fires for `SectionMeetingBlock`-derived entries instead of
`SharedScheduleBlock`-derived ones.

**Click behavior**: Shared entries now have
`IsCommitment = false`, so they ARE clickable (selection).
The existing click handler sets `SectionId` on `SectionStore`,
which cross-highlights the section list and workload panel.
Add a guard in the right-click handler:
```csharp
if (entry.IsSharedSchedule)
{
    e.Handled = true;
    return;  // no context menu for shared sections
}
```

**Cursor**: Shared entries get the hand cursor (they're
selectable). This is a change from today (no cursor).

**Tooltip**: Shared entries now flow through the standard
`BuildTileTooltip` path. The tooltip shows course code, section
code, time, and — for shared entries — appends the source label
line. The tooltip builder checks `entry.IsSharedSchedule` (or
`entry.SourceLabel != ""`) to append it.

**Flag icon**: Shared entries have `Flag = SectionFlag.None`
(no flag assigned). The renderer's flag-icon path naturally
skips `None`.

#### `SectionListView.axaml`

**Shared section card styling:**

Shared section cards use the same `DataTemplate` as local cards
but with conditional styling:
- **Purple text**: bind `Foreground` to a style trigger on
  `IsShared`. When true, use `SharedScheduleText` brush for
  the heading and schedule lines.
- **Source label badge**: a `TextBlock` bound to `SourceLabel`,
  visible when `IsShared` is true. Small font, purple text,
  shown after the heading (e.g., "(Chemistry)").
- **No expand arrow**: the expand/collapse toggle is hidden
  when `IsShared` is true.
- **No context menu**: `ContextMenu` is null when `IsShared`.
- **Conflict warnings**: rendered identically to local cards
  (same warning `TextBlock` bound to `RoomConflictWarning` /
  `InstructorConflictWarning`).

These can be implemented with Avalonia `Style` selectors
using a `Classes` binding on the card's root panel:
```xml
<Border Classes.shared="{Binding IsShared}">
```

#### `WorkloadPanelView.axaml`

**Shared section chips:**
- Purple text: style trigger on `IsShared`
- Source label: already included in `Label` (e.g.,
  "CHEM101 A (Chemistry)")
- Non-clickable: `PointerPressed` handler checks `IsShared`
  and suppresses selection

#### `SharedScheduleStripView.axaml`

**Enhanced collapsed summary**: bind to updated
`CollapsedSummary` property (includes resolution stats).

**Enhanced expanded rows**: update the row `DataTemplate` to
show instructor initials and room shorthand. Bind to new
properties on `SharedScheduleSourceRow`.

**Resolution summary header**: add a `TextBlock` per source
group header showing the matched counts.

#### `SharingView.axaml` (Sharing Flyout)

**Import status message**: multi-line text already renders in
the existing `TextBlock`. The `StatusMessage` string gains
resolution summary lines. No template change needed unless
styled formatting (e.g., bold counts) is desired — plain
text is sufficient for now.

**Export button tooltip in multi-semester mode**: "Select a
single semester to use shared schedules" — the disabled state
and tooltip are bound to a new `IsSingleSemester` property
on `SharingViewModel` (derived from
`_semesterContext.SelectedSemesterIds.Count == 1`).

### Model Retirement

The following types are removed entirely:

| Type | File | Replaced By |
|------|------|-------------|
| `SharedSection` | `Models/SharedSection.cs` | `Section` with `IsShared = true` |
| `SharedMeeting` | `Models/SharedMeeting.cs` | `SectionDaySchedule` |
| `SharedScheduleBlock` | `GridData.cs` (line 122) | `SectionMeetingBlock` with `IsSharedSchedule = true` |

The `SharedScheduleBlock` record is deleted from `GridData.cs`.
All code that pattern-matches on it (`ToEntry`, `DeduplicateBlocks`,
`BuildSharedScheduleTooltip`) is updated or removed.

`SharedScheduleSet` survives but its `Sections` property type
changes from `List<SharedSection>` to `List<Section>`.

### DI Registration Changes

**New registrations** (in `App.axaml.cs`, both desktop and WASM
paths):
```csharp
services.AddSingleton<ImportResolver>();
```

`ImportResolutionIndex` is not registered — it's constructed via
a static factory method in the import flow, not injected.

`ExportLookups` is not registered — it's a data carrier
constructed inline.

`ImportResolutionSummary` is not registered — it's a return value.

**Modified registrations:** None. `SharedScheduleService`,
`SharedScheduleCsvParser`, and `SharedScheduleCsvExporter` keep
their existing registrations. `SharingViewModel` stays transient.

**New constructor parameters on existing registrations:**
- `SharingViewModel`: +4 repos + `ImportResolver`
- `SectionListViewModel`: + `SharedScheduleService`
- `WorkloadPanelViewModel`: + `SharedScheduleService`
- `SharedScheduleStripViewModel`: + `IRoomRepository`

All injected types are already registered. No circular dependency
risks — all new dependencies are on repositories (transient) or
`SharedScheduleService` (singleton, no dependency on VMs).

### Invariants and Safety

1. **Filter option isolation (C2)**: `PopulateFilterOptions` reads
   from `GridLookups`, which is built from local data only. Shared
   sections are merged into `allSections` after `BuildLookups` but
   before `BuildFilteredBlocks`. The merge is a local variable in
   `ReloadCore`, not a mutation of `GridLookups`.

2. **Data isolation (C1)**: shared sections never enter
   `SectionStore`, never pass to any repository, never appear in
   any save/export-of-A's-data operation. The `IsShared` flag
   provides defense-in-depth.

3. **Single-semester constraint**: `SharingViewModel` checks
   `_semesterContext.SelectedSemesterIds.Count == 1` before
   enabling Import/Export. `SharedScheduleService` stores sections
   with a stamped `SemesterId`; `GetSectionsForSemester` filters
   by it.

4. **Selection safety**: clicking a shared section in the grid
   sets selection to the synthetic GUID. The section list highlights
   the corresponding shared card. The section editor does NOT open
   (guarded by `IsShared` on the `SectionListItemViewModel`).

5. **Save safety**: any code path that writes a `Section` to the
   database (repository save, copy, delete) must check `IsShared`
   and refuse. Today, the collection boundary already prevents
   this (shared sections are never in `SectionStore`). The
   `IsShared` flag is defense-in-depth for any future code that
   might receive a `Section` from an unexpected source.

6. **Conflict detection correctness**: shared sections with
   `CourseId = null` correctly group separately from all of A's
   courses in `ProgramConflictService`. Course-mode watches
   exclude them naturally (`courseIdSet.Contains(null)` is false).
   Tag-mode watches include them via resolved `TagIds`.

### Open Questions for Phase 6 — RESOLVED

1. **Implementation ordering** — RESOLVED in Phase 6.
2. **Test strategy** — RESOLVED in Phase 6.
3. **Migration of existing shared schedule files** — RESOLVED in
   Phase 6 (Session 8 verification step).

---

## Phase 6 — Implementation Plan
**Status: LOCKED** — signed off 2026-07-15

This phase breaks the architecture into ordered implementation
sessions, identifies dependencies, and defines the test strategy.
Each session is scoped to roughly one working session (~2–3 hours).

### Resolved: Open Questions from Phase 5

**Q1 — Implementation ordering.** The dependency graph has four
tiers:

```
Tier 0: Model changes (Section, SectionDaySchedule, records, enums)
   ↓
Tier 1: Core services (ImportResolutionIndex, ImportResolver,
         Parser rewrite, Exporter enrichment, SharedScheduleService)
   ↓
Tier 2: ViewModel integration (SharingVM, GridVM, SectionListVM,
         WorkloadVM, StripVM, SectionListItemVM)
   ↓
Tier 3: View layer + cleanup (AXAML styling, model deletion,
         DI registration)
```

Within each tier, some work is independent. The exporter
enrichment (Tier 1) has no dependency on the parser rewrite
or the resolution index. The grid pipeline restructuring (Tier 2)
depends on SharedScheduleService changes but not on the section
list or workload panel changes.

Sessions are ordered to deliver compilable, testable increments
at each step. No session leaves the codebase in a broken state.

**Q2 — Test strategy.** Three categories:

- **Unit tests** for pure-function services: `ImportResolutionIndex`
  (normalization, tiebreakers, ambiguity), `ImportResolver`
  (full resolution flow, summary counts), parser (18-column and
  9-column backward compat), exporter (18-column output). These
  follow the existing pattern of instantiating the class directly
  with no mocking.

- **Extended pipeline tests** for `BuildFilteredBlocks`: add cases
  for `IsShared` sections with Course/Subject filter exemption
  (C4), using the existing `Sec()`/`Slot()`/`Snap()` helpers
  from `GridPipelineTests.cs`. Also extend `BuildSectionLabel`
  tests for shared sections.

- **Manual integration testing** in Session 8: import/export
  round-trip with enriched CSV, filter behavior with shared
  sections, cross-panel selection, conflict detection across
  local + shared sections, workload panel shared chips, strip
  resolution stats. No automated integration test — the feature
  spans too many UI components for a non-UI test harness.

No shared `SectionBuilder` extraction — the existing per-file
helper pattern is sufficient. The new test files will define
their own helpers following the same conventions.

**Q3 — Migration of existing shared schedule files.** Session 8
includes a manual verification step: import a 9-column CSV
produced by the current exporter, confirm it loads with empty
enriched properties, confirm shared sections are excluded by
any active property filter (correct degraded behavior), and
confirm the status message says "Matched: 0 instructors, 0 rooms,
0 tags." No code migration needed — backward compatibility is
built into the parser.

### Session 1: Model Foundation

**Goal**: All model/record/enum changes that downstream sessions
depend on. Compiles, existing tests pass, no behavioral changes.

**Tasks:**

1. **Section model** — add 9 `[JsonIgnore]` properties:
   - 6 display: `IsShared`, `SourceLabel`, `DisplayCourseCode`,
     `DisplayInstructors`, `RoomConflictNote`,
     `InstructorConflictNote`
   - 3 import-staging: `ImportedCampusName`,
     `ImportedSectionTypeName`, `ImportedTagNames`

2. **SectionDaySchedule model** — add 3 `[JsonIgnore]` properties:
   `ImportedBuilding`, `ImportedRoomNumber`,
   `ImportedMeetingTypeName`

3. **ImportResolutionSummary** — new record in
   `Models/ImportResolutionSummary.cs`

4. **ExportLookups** — new record in `Services/ExportLookups.cs`

5. **SharedScheduleSet** — change `Sections` from
   `List<SharedSection>` to `List<Section>`. This breaks
   `SharedScheduleService.BuildBlocks()` and
   `SharedScheduleStripViewModel` — stub them to compile:
   - `BuildBlocks`: adapt to read from `Section` properties
     (`DisplayCourseCode`, `SectionCode`, `Schedule`) instead of
     `SharedSection` properties. This is a temporary bridge that
     keeps the grid working until Session 6 removes it.
   - `SharedScheduleStripViewModel.Refresh()`: adapt row
     construction to read from `Section` instead of
     `SharedSection`.

6. **SectionMeetingBlock** — add `IsSharedSchedule = false`
   parameter to the record.

7. **TileEntry** — add `SourceLabel = ""` parameter to the record.

8. **WorkloadItemKind** — add `SharedSection` value.
   **WorkloadItemViewModel** — add `IsShared` computed property.

9. **SharedScheduleCsvParser** — update `ImportResult` record to
   add `SemesterName` field (default `null`). Existing parse
   logic unchanged — still produces `Section` objects (the
   `SharedScheduleSet.Sections` type change forces the parser
   to construct `Section` instead of `SharedSection`). The parser
   constructs `Section` objects with `IsShared = true`,
   `DisplayCourseCode`, `SectionCode`, etc. from the existing
   9 columns. Enriched column parsing is deferred to Session 3.

10. **Compile check** + **full test suite** — verify zero
    regressions. The `SharedScheduleCsvParserTests` and
    `SharedScheduleCsvExporterTests` will need updates to match
    the new `Section`-based output.

**Exit criteria**: `dotnet build -f net10.0 -t:Compile` passes.
`dotnet test` passes (all existing tests, updated as needed for
the `SharedSection` → `Section` type change).

### Session 2: ImportResolutionIndex + ImportResolver

**Goal**: The two new pure-function services, fully tested.

**Tasks:**

1. **ImportResolutionIndex** — implement
   `Services/ImportResolutionIndex.cs`:
   - `Normalize()` static method
   - `Build()` factory constructing all 7 dictionaries
   - 6 `Resolve*` methods with the matching rules from Phase 2
   - Custom `IEqualityComparer` for `(string, string)` tuple keys
     with `OrdinalIgnoreCase`

2. **ImportResolver** — implement `Services/ImportResolver.cs`:
   - `Resolve()` method: iterates sections, calls index lookups,
     populates entity IDs, builds `ImportResolutionSummary`
   - Instructor: split `"LastName, FirstName"`, resolve, create
     `InstructorAssignment`
   - Room: read `ImportedBuilding`/`ImportedRoomNumber` from
     `SectionDaySchedule`, resolve, set `RoomId`
   - Campus/SectionType/Tags/MeetingType: read `Imported*` from
     `Section`/`SectionDaySchedule`, resolve, set IDs

3. **Unit tests** — `ImportResolutionIndexTests.cs`:
   - Normalize: trim, collapse whitespace, empty/null handling
   - Instructor: single match, multiple same-name with initials
     tiebreaker, ambiguous (same name + same initials), no match
   - Room: composite match, fallback to RoomNumber-only when
     building empty, ambiguous (same number different buildings)
   - Campus/SectionType/Tag/MeetingType: simple name match,
     no match, case-insensitive
   - Collision handling: duplicate rooms logged, duplicate tags
     last-write-wins

4. **Unit tests** — `ImportResolverTests.cs`:
   - Full section resolution with mixed resolved/unresolved
   - Summary counts (distinct names, not occurrences)
   - Warnings for ambiguous matches
   - Multiple instructors per section
   - Multiple tags, partial match
   - Room resolution per-meeting (different rooms on different
     days)

**Exit criteria**: All new tests pass. Compile check passes.

### Session 3: Parser Rewrite

**Goal**: The parser reads all 18 columns, produces fully
populated (but unresolved) `Section` objects.

**Tasks:**

1. **SharedScheduleCsvParser** — extend parsing:
   - Read new header format with semester name (3rd field)
   - Column dictionary recognizes columns 10–18 by header name
   - Parse Instructor + Initials (pipe-split, zip by position)
     → `DisplayInstructors`
   - Parse Building, RoomNumber → `ImportedBuilding`,
     `ImportedRoomNumber` on each `SectionDaySchedule`
   - Parse Campus → `ImportedCampusName`
   - Parse SectionType → `ImportedSectionTypeName`
   - Parse Tags (pipe-split) → `ImportedTagNames`
   - Parse MeetingType → `ImportedMeetingTypeName` on each
     `SectionDaySchedule`
   - Parse Level → `Section.Level`
   - Per-section fields from first row only; per-meeting fields
     per row

2. **Backward compatibility** — missing columns (old 9-column
   CSV) → all `Imported*` fields stay null/empty,
   `DisplayInstructors` is null. Existing parser tests must
   still pass (they use 9-column input).

3. **Update existing parser tests** — `SharedScheduleCsvParserTests`:
   - Verify existing tests pass with the `Section`-based output
     (assertions reference `Section` properties instead of
     `SharedSection` properties)
   - Add new tests for 18-column parsing: pipe-delimited
     instructors, building/room per meeting, tags, semester
     header, mixed old/new format

**Exit criteria**: All parser tests pass (old + new). Compile
check passes.

### Session 4: Exporter Enrichment

**Goal**: The exporter writes all 18 columns with semester name
in the header.

**Tasks:**

1. **SharedScheduleCsvExporter** — extend `Export()`:
   - Add `ExportLookups lookups` and `string semesterName`
     parameters
   - Write semester name in header comment (3rd field)
   - Write 18-column header row
   - Per-section: resolve Instructor, Initials, Campus,
     SectionType, Tags, Level from lookups
   - Per-meeting: resolve Building, RoomNumber, MeetingType
     from lookups
   - Missing data → empty string

2. **SharingViewModel** — update `ExportSharedSchedule`:
   - Add 4 new constructor dependencies
     (`IInstructorRepository`, `IRoomRepository`,
     `ICampusRepository`, `ISchedulingEnvironmentRepository`)
   - Build `ExportLookups` from repo `GetAll()` calls
   - Get semester name from `_semesterContext`
   - Pass lookups + semester name to exporter

3. **DI registration** — no new registrations needed (repos
   already registered). SharingViewModel constructor change is
   automatic.

4. **Update existing exporter tests** —
   `SharedScheduleCsvExporterTests`:
   - Update `Export()` helper to pass `ExportLookups` and
     semester name
   - Add new tests for enriched columns: instructor formatting,
     pipe-delimited multi-value, building/room per meeting,
     tags, missing data (null IDs → empty), semester in header

5. **Round-trip test** — write a test that exports a section
   with enriched data, then parses the output and verifies the
   parsed `Section` has the expected `DisplayInstructors`,
   `ImportedBuilding`, `ImportedTagNames`, etc.

**Exit criteria**: All exporter tests pass (old + new). Round-trip
test passes. Compile check passes.

### Session 5: SharedScheduleService + Import Orchestration

**Goal**: The full import pipeline works end-to-end: parse →
resolve → stamp semester → add to service → status message.

**Tasks:**

1. **SharedScheduleService** — changes:
   - Remove `BuildBlocks()` (still needed by grid until Session 6
     — keep it but deprecate, or keep the bridge from Session 1)
   - Add `GetSectionsForSemester(string semesterId)` method
   - No internal type changes (already uses `List<Section>` from
     Session 1)

   Actually, `BuildBlocks()` is still called by
   `ScheduleGridViewModel` until Session 6 removes that call.
   So keep it in Session 5; remove it in Session 6.

2. **ImportResolver DI** — register `ImportResolver` as singleton
   in `App.axaml.cs` (both desktop and WASM paths).

3. **SharingViewModel** — update `ImportSharedSchedule`:
   - After parse: build `ImportResolutionIndex` from 6 repo calls
   - Call `_resolver.Resolve(sections, index)`
   - Stamp `SemesterId` on all sections
   - Check semester name mismatch → append warning
   - Build `SharedScheduleSet` with `ResolutionSummary`
   - Multi-line status message with resolution counts

4. **SharingViewModel** — add `IsSingleSemester` property,
   bind Import/Export enabled state to it.

5. **Compile check** + test suite — verify the import
   orchestration compiles and existing tests pass.

**Exit criteria**: Import pipeline compiles and is wired end-to-end.
The grid still uses `BuildBlocks()` (bridge from Session 1), so
visual behavior is unchanged. `GetSectionsForSemester` is available
for Sessions 6–7.

### Session 6: Grid Pipeline Restructuring

**Goal**: Shared sections flow through `BuildFilteredBlocks` as
regular sections. Pass 4 is eliminated. Grid renders shared tiles
via `SectionMeetingBlock` with `IsSharedSchedule`.

**Tasks:**

1. **ScheduleGridViewModel.ReloadCore** — merge shared sections:
   - After `BuildLookups`, get shared sections via
     `_sharedScheduleService.GetSectionsForSemester(semId)`
   - Build `allSections = lookups.Sections.Concat(shared).ToList()`
   - Pass `allSections` to `BuildFilteredBlocks`

2. **BuildFilteredBlocks** — changes:
   - Accept `IReadOnlyList<Section> sections` parameter (decouple
     from `lookups.Sections`)
   - Course filter: add `!section.IsShared` guard (C4)
   - Subject filter: add `!section.IsShared` guard (C4)
   - Create `SectionMeetingBlock` with
     `IsSharedSchedule: section.IsShared`

3. **BuildSectionLabel** — add `IsShared` branch:
   - Label from `DisplayCourseCode + SectionCode`
   - Initials from `DisplayInstructors`

4. **ToEntry** — changes:
   - `SectionMeetingBlock` case: propagate `IsSharedSchedule`,
     set `SourceLabel` from a lookup dictionary
   - Remove `SharedScheduleBlock` case

5. **DeduplicateBlocks** — remove `SharedScheduleBlock` case.

6. **Remove** `BuildSharedScheduleBlocks` method.
   **Remove** `BuildSharedScheduleTooltip` method.
   **Remove** the `BuildBlocks()` bridge on `SharedScheduleService`.

7. **ReloadCore** — remove Pass 4 (sharedBlocks) from
   `combinedBlocks` concatenation.

8. **ProgramConflictService call site** — verify shared sections
   are in `visibleSections` and `tagIdsBySectionId` (they should
   be, since they flow through `BuildFilteredBlocks`). Extend
   `courseCodeById` for shared sections.

9. **ScheduleGridView.axaml.cs** — changes:
   - Right-click handler: suppress context menu for
     `IsSharedSchedule` entries
   - Tooltip: append `SourceLabel` for shared entries
   - Shared entries are now selectable (no `IsCommitment` block)

10. **Extend GridPipelineTests** —
    `BuildFilteredBlocksTests`:
    - Shared section passes Course filter (C4 exemption)
    - Shared section passes Subject filter (C4 exemption)
    - Shared section filtered by Tag/Instructor/Room normally
    - `BuildSectionLabel` for shared section (DisplayCourseCode,
      DisplayInstructors)

11. **Delete SharedScheduleBlock** record from `GridData.cs`.

**Exit criteria**: Grid renders shared tiles via the unified
pipeline. Purple text, selectable, no context menu. Filters apply
correctly with C4 exemption. All pipeline tests pass. Compile
check passes.

### Session 7: Section List + Workload Panel

**Goal**: Shared sections appear in the section list and workload
panel with the correct rendering and interaction behavior.

**Tasks:**

1. **SectionListItemViewModel** — changes:
   - Add `IsShared` (`bool`, `init`)
   - Add `SourceLabel` (`string?`)
   - `ToggleCollapsed`: early-return when `IsShared`
   - Heading: use `DisplayCourseCode` for shared sections
   - InstructorLine: use `DisplayInstructors` for shared sections
   - Flag operations: refuse when `IsShared`

2. **SectionListViewModel** — changes:
   - New dependency: `SharedScheduleService`
   - `LoadCore`: after sorting local items, append shared sections
     sub-grouped by source, sorted by `(DisplayCourseCode,
     SectionCode)`
   - `CreateSharedSectionItem`: new helper method
   - `ApplyRoomConflicts` / `ApplyInstructorConflicts`: merge
     shared sections, extend `courseCodeById`, route conflict
     descriptions to `Section.RoomConflictNote` /
     `InstructorConflictNote` for shared sections
   - Subscribe to `_sharedScheduleService.Changed`

3. **SectionListView.axaml** — shared card styling:
   - Purple text via `Classes.shared` binding
   - Source label badge
   - Hide expand arrow when shared
   - Null context menu when shared
   - Conflict warnings render normally

4. **WorkloadPanelViewModel** — changes:
   - New dependency: `SharedScheduleService`
   - `Load`: merge shared sections
   - `BuildItemsForInstructor`: create `SharedSection` kind items
     with source label suffix, zero workload
   - Instructor conflict detection: merge shared sections
   - Subscribe to `_sharedScheduleService.Changed`

5. **WorkloadPanelView.axaml** — shared chip styling:
   - Purple text via `IsShared` trigger
   - Click suppression

6. **DI registration** — add `SharedScheduleService` to
   `SectionListViewModel` and `WorkloadPanelViewModel` constructor
   calls. Add `ImportResolver` registration if not done in
   Session 5.

**Exit criteria**: Shared sections visible in section list (purple,
non-expandable, source badge, conflict warnings). Shared chips
visible in workload panel (purple, non-clickable, source label,
zero workload). Cross-panel selection works (click shared tile on
grid → highlights card in list). Compile check + test suite pass.

### Session 8: Strip Enhancement + Cleanup + Verification

**Goal**: Strip shows resolution stats. Old models deleted.
End-to-end verification.

**Tasks:**

1. **SharedScheduleStripViewModel** — changes:
   - New dependency: `IRoomRepository`
   - `CollapsedSummary`: include resolution stats per source
   - `SharedScheduleSourceRow`: build from `Section` with
     instructor initials and room shorthand
   - Source group header: show resolution summary line

2. **SharedScheduleStripView.axaml** — changes:
   - Enhanced collapsed summary binding
   - Enhanced expanded rows (initials + room)
   - Resolution summary header per source group

3. **SharingView.axaml** — changes:
   - Multi-semester tooltip on Import/Export buttons
   - Status message already multi-line (no template change)

4. **Delete retired models**:
   - `Models/SharedSection.cs`
   - `Models/SharedMeeting.cs`
   - Remove any remaining references

5. **DI registration audit** — verify both desktop and WASM
   paths in `App.axaml.cs` have:
   - `ImportResolver` registered
   - Updated constructor parameter lists compile
   - WASM `ConfigureDemoServices` mirrors desktop

6. **Compile check** + **full test suite**

7. **Manual verification checklist**:
   - [ ] Export enriched CSV: verify 18 columns, semester in
     header, pipe-delimited instructors/tags
   - [ ] Import enriched CSV: verify resolution summary in
     status message, shared sections in list/grid/workload
   - [ ] Import old 9-column CSV: verify backward compat,
     "Matched: 0 instructors, 0 rooms, 0 tags" message,
     shared sections excluded by property filters
   - [ ] Filter behavior: Course/Subject filters don't exclude
     shared sections (C4). Tag/Instructor/Room filters do
     apply. Filter options don't include shared data (C2).
   - [ ] Conflict detection: room conflict between local and
     shared section shows warning on both cards. Instructor
     conflict same. Access watch (tag-mode) includes shared
     sections.
   - [ ] Selection: click shared tile on grid → card highlights
     in section list, chip highlights in workload panel.
     No editor opens. No context menu.
   - [ ] Strip: collapsed summary shows resolution stats.
     Expanded view shows initials + room shorthand.
   - [ ] Multi-semester: Import/Export disabled with tooltip.
   - [ ] Dismiss: dismiss one source, dismiss all — grid/list/
     workload update correctly.

**Exit criteria**: All tests pass. Manual verification checklist
complete. Feature is shippable.

### Dependency Graph

```
Session 1: Model Foundation
    ↓
Session 2: ImportResolutionIndex + ImportResolver
    ↓
Session 3: Parser Rewrite ←──── depends on Session 1 (Section model)
    ↓                           + Session 2 (for round-trip validation)
Session 4: Exporter Enrichment ← depends on Session 1 (ExportLookups)
    ↓
Session 5: Import Orchestration ← depends on Sessions 2, 3, 4
    ↓
Session 6: Grid Pipeline ←────── depends on Session 5
    ↓                             (GetSectionsForSemester)
Session 7: Section List + ←───── depends on Session 5
           Workload Panel         (GetSectionsForSemester)
    ↓
Session 8: Strip + Cleanup ←──── depends on Sessions 6, 7
```

Sessions 3 and 4 are independent of each other (both depend only
on Session 1). Sessions 6 and 7 are independent of each other
(both depend on Session 5). This allows parallel work if desired,
but the recommended order is sequential as listed — each session
builds confidence incrementally.

### Risk Notes

- **Largest risk**: Session 6 (grid pipeline). The `ReloadCore`
  method is ~400 lines with multiple interleaved passes.
  `BuildFilteredBlocks` is a ~100-line static method with 8
  filter dimensions. The changes are mechanical (add parameters,
  add guards) but the method is dense. The existing
  `GridPipelineTests` provide good coverage for regressions.

- **AXAML warning**: Sessions 7 and 8 edit `.axaml` files. Per
  project convention, ask the user to close the relevant tabs in
  VS 2022 before editing (VS won't auto-reload AXAML).

- **WASM parity**: Session 8's DI audit must verify that
  `ConfigureDemoServices` in the WASM path mirrors the desktop
  `ConfigureServices`. Missing registrations in the WASM path
  have caused runtime crashes before.

### Test Summary

| Component | Test Type | File | Session |
|-----------|-----------|------|---------|
| ImportResolutionIndex | Unit | ImportResolutionIndexTests.cs | 2 |
| ImportResolver | Unit | ImportResolverTests.cs | 2 |
| SharedScheduleCsvParser (18-col) | Unit | SharedScheduleCsvParserTests.cs (extended) | 3 |
| SharedScheduleCsvExporter (18-col) | Unit | SharedScheduleCsvExporterTests.cs (extended) | 4 |
| Export→Parse round-trip | Unit | SharedScheduleRoundTripTests.cs | 4 |
| BuildFilteredBlocks (C4 exemption) | Unit | GridPipelineTests.cs (extended) | 6 |
| BuildSectionLabel (IsShared) | Unit | GridPipelineTests.cs (extended) | 6 |
| End-to-end feature | Manual | Checklist in Session 8 | 8 |
