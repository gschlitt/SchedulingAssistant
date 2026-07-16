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
