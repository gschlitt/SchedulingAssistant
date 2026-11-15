# Schedule Overlay (Cross-Department Visibility)

## Phase 1 — Problem Statement
**Status: LOCKED** — signed off 2026-05-16

### Who
University department schedulers whose departments share students but maintain independent TermPoint databases. There is no shared infrastructure and no IT involvement.

### Problem
Departments that share students have no way to see each other's schedules. A Biology scheduler and a Chemistry scheduler may unknowingly place required courses at the same time. Conflicts are invisible until students try to register — by which point the schedule is often locked.

Today, coordination (if it happens at all) is informal: emails, hallway conversations, spreadsheets passed around. There is no structured, repeatable way to share schedule data between TermPoint users.

### Constraints
- **No shared database.** Each user has their own local SQLite file. This is a feature, not a limitation — it means zero IT overhead.
- **No server or network service.** The only communication channels available are email and a shared filesystem (e.g., a department network drive).
- **Zero admin to set up.** If it requires a taskforce or IT ticket, it won't get used.
- **Import must be non-destructive.** Nothing imported touches the receiving user's database. Overlay data lives in memory only and is transient.

### Proposed Approach
**CSV export/import.** User B (the sender) exports relevant sections as a CSV file. User A (the receiver) imports that file, and the sections appear as a read-only overlay on their schedule grid.

### Export Side (User B)
- Export is driven by the current filter state. Whatever sections are visible in User B's section list at the moment of export become the exported set.
- User B tunes their filters before exporting to include only what's relevant to the recipient.
- The exported file is a simple CSV that User B can email or drop on a shared drive.

### Import Side (User A)
- User A imports the CSV file. Imported sections appear as a visual overlay on the schedule grid.
- Overlay sections are visually distinct from User A's own sections (styling TBD in Phase 3).
- Overlay data is **transient** — it lives in memory only, never written to User A's database.
- Closing the app (or dismissing the overlay) discards the data. No persistence.

### Data in the Export
Minimum viable fields per section:
- Course code
- Section code
- Meeting day(s)
- Meeting start time
- Meeting duration

Deliberately excluded (at least initially):
- **Instructor** — potential privacy concern; not needed to spot time conflicts
- **Room** — belongs to the sender's campus; not meaningful to the receiver
- **Internal IDs / GUIDs** — not portable across databases

### Known Design Tension
Section properties (tags, section types, etc.) are referenced internally by GUID. User B's "UpperLevel" tag has a different GUID than User A's "UpperLevel" tag, even if the display name is identical. This means:
- Filtering imported sections by the receiver's own property categories is non-trivial.
- Phase 2 needs to decide whether overlay sections carry property display names (for human reading / simple text matching) or whether filtering is out of scope for v1.

### What "Done" Looks Like
1. User B can export their currently-filtered sections to a CSV with one menu action.
2. User A can import that CSV and see the sections overlaid on their schedule grid.
3. User A's database is untouched — overlay is purely in-memory.
4. The entire workflow requires zero IT involvement: just a file and an email.

### What "Done" Does NOT Include (v1)
- Automatic refresh or live sync
- Conflict detection or alerting
- Round-trip editing (User A cannot modify overlay sections)
- Filtering overlay sections by property categories

### Open Questions for Phase 2 — RESOLVED
1. **Property display names in CSV?** → No. CSV carries only scheduling geometry + identifiers + notes. No tags, section types, or other property values.
2. **Metadata header?** → Yes. A `#`-prefixed comment line with source label and export date. Optional on import (defaults to filename if absent).
3. **Survive semester switches?** → No. Overlay is dismissed on semester change. The data is semester-specific; showing it against a different semester would be misleading.

---

## Phase 2 — Domain Model
**Status: LOCKED** — signed off 2026-05-16

### Entities (In-Memory Only — Never Persisted)

**OverlaySet** — container for one import operation
| Field | Type | Notes |
|-------|------|-------|
| SourceLabel | string | From header comment, or filename if header absent |
| ExportedAt | DateTime? | From header comment; nullable |
| Sections | List\<OverlaySection\> | The imported sections |

**OverlaySection** — one logical section from the overlay CSV
| Field | Type | Notes |
|-------|------|-------|
| CourseCode | string | Human-readable course identifier |
| SectionCode | string | Human-readable section identifier |
| Notes | string? | Optional freeform notes from sender |
| Meetings | List\<OverlayMeeting\> | One entry per meeting occurrence |

**OverlayMeeting** — one day/time slot
| Field | Type | Notes |
|-------|------|-------|
| Day | int | 1=Mon … 6=Sat (matches SectionDaySchedule) |
| StartMinutes | int | Minutes from midnight |
| DurationMinutes | int | Duration in minutes |
| Frequency | string? | null=every week, "odd", "even", or "1,6,7" |

Computed: `EndMinutes => StartMinutes + DurationMinutes`

### CSV Format

```
#TermPoint Schedule Overlay,Chemistry Department,2026-05-16
CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency
CHEM101,A,,Monday,8:00 AM,8:50 AM,50,480,
CHEM101,A,,Wednesday,8:00 AM,8:50 AM,50,480,
CHEM101,A,,Friday,8:00 AM,8:50 AM,50,480,
CHEM201,B,Lab requires goggles,Tuesday,10:00 AM,11:20 AM,80,600,
CHEM201,B,Lab requires goggles,Tuesday,10:00 AM,11:20 AM,80,600,
CHEM310,A,,Monday,1:00 PM,2:50 PM,110,780,odd
CHEM310,A,,Monday,1:00 PM,1:50 PM,50,780,even
```

**Design principles (following BackupService pattern):**
- Human-readable AND machine-parseable in the same row — no separate file sections
- Day: full English name (Monday, Tuesday…) for human reading; parser maps to int
- StartTime/EndTime: `h:mm tt` format for human reading; parser uses StartMinutes + DurationMin
- One row per meeting occurrence (MWF section = 3 rows)
- Notes repeat on every row of the same section (same pattern as BackupService repeating section-level columns)
- RFC-4180 compliant (quoted fields for commas/newlines in notes)

**Header comment (line 1, optional on import):**
`#TermPoint Schedule Overlay,<source label>,<ISO date>`

### Grid Integration

Each `OverlayMeeting` maps to an existing `SectionMeetingBlock`:
- `IsOverlay = true` (existing flag — controls styling and suppresses selection/editing)
- `Label = "{CourseCode} {SectionCode}"`
- `Initials = ""` (no instructor data)
- `FrequencyAnnotation = FormatFrequency(Frequency)`
- `SectionId = ""` (no local ID)

No new GridBlock subtype required. Existing overlay rendering pipeline handles it.

### Overlay List Panel

Imported sections are browsable in a **dedicated read-only panel**, separate from Section List. This panel:
- Shows overlay sections (course code, section code, schedule summary, notes)
- Is non-editable — no expand, no inline form, no interaction with local data
- Is lightweight and uncluttered (UX details deferred to Phase 3)

**Hard boundary:** Overlay data never enters Section List, SectionEditViewModel, or any local-data code paths.

### Service Architecture

**OverlayService** — injected singleton holding the active `OverlaySet` (or null).
- Grid VM queries the service for overlay blocks to mix into the day columns
- Overlay list panel binds to the service's section collection
- Service exposes `Import(stream)`, `Dismiss()`, and state-change notifications
- `Dismiss()` called automatically on semester switch

### Lifetime Rules

- Created on CSV import, held in memory by OverlayService
- Dismissed on: app close, semester switch, or explicit user action (per-overlay or dismiss-all)
- Never written to the recipient's database
- Multiple overlays may be active simultaneously (e.g., Chemistry + Biology + Nursing)
- OverlayService holds `List<OverlaySet>`; grid receives a flat stream of blocks from all active sets
- Each OverlaySet retains its SourceLabel so the UX layer can distinguish sources if desired (Phase 3 decision)

### Export Side (Domain)

Export produces the CSV from the current filter state:
- Query: all sections currently visible in Section List (respecting active filters)
- For each section: emit one row per meeting in its Schedule list
- Notes carried from `Section.Notes`
- No instructor, room, or internal IDs emitted

### What Phase 2 Does NOT Define
- Visual styling of overlay tiles (Phase 3 — UX)
- Overlay list panel layout and controls (Phase 3 — UX)
- Menu item placement for import/export (Phase 3 — UX)
- File dialog behavior and error handling (Phase 4 — Data)
- OverlayService implementation details and DI registration (Phase 5 — Architecture)

---

## Phase 3 — UX
**Status: LOCKED** — signed off 2026-05-16

### Terminology Decision

The app already uses "overlay" for the filter bar's instructor/room/tag emphasis feature (red highlight). To avoid confusion, the cross-department CSV feature is named **"Shared Schedule"** throughout the UI:

- Menu items: "Import Shared Schedule…", "Export Shared Schedule…"
- Filter bar strip: "Shared Schedules"
- Internal types: `SharedScheduleService`, `SharedScheduleSet` (renamed from `OverlayService`, `OverlaySet`)
- Domain model types (`OverlaySection`, `OverlayMeeting`) also renamed to `SharedSection`, `SharedMeeting`

The existing filter bar overlay feature (instructor/room/tag) keeps its current name and behavior unchanged.

> **Phase 2 amendment:** The grid integration section specified `IsOverlay = true` to reuse the existing overlay rendering path (red text/border). Phase 3 replaces this: shared schedule tiles use a dedicated `IsSharedSchedule` flag and a distinct purple/outlined rendering path. `IsOverlay` remains reserved for the emphasis overlay feature.

### Color Palette

All shared schedule visuals use a purple/violet family, distinct from the red emphasis overlay (#D63050) and green ghost blocks (#4CAF50).

| Resource Key | Value | Usage |
|---|---|---|
| `SharedScheduleColor` | `#7C5CCC` | Primary accent — border, text |
| `SharedScheduleBorder` | SolidColorBrush of SharedScheduleColor | Tile border (2px), strip accent |
| `SharedScheduleText` | SolidColorBrush of SharedScheduleColor | Tile label text |
| `SharedScheduleBodyFill` | `#0C7C5CCC` | Very faint purple wash inside tile (nearly transparent) |
| `SharedScheduleBadgeText` | SolidColorBrush of SharedScheduleColor | Summary badge in filter bar |
| `SharedScheduleStripBackground` | `#F5F0FF` | Collapsible strip background |

### Grid Tile Rendering

Shared schedule tiles are **outlined** — purple border with transparent body, purple text. They sit alongside local sections without competing for visual attention.

**Tile structure:**
- Border: 2px `SharedScheduleBorder`, CornerRadius 3
- Body fill: `SharedScheduleBodyFill` (nearly transparent purple)
- Text: `SharedScheduleText` (purple), FontWeight Normal (not Bold)
- Label format: `"{CourseCode} {SectionCode}"` — same single-line pattern as local tiles
- No initials (no instructor data in shared schedules)
- Frequency annotation displayed if present (e.g., "odd wks")

**Interaction:**
- No selection — clicking a shared tile does nothing (no highlight, no context menu)
- No hover cursor change (stays default arrow, not hand)
- Tooltip on hover: `"{CourseCode} {SectionCode}\n{SourceLabel}\n{time range}"` — and Notes if present

**Overlap with local sections:**
- Shared tiles participate in the existing overlap layout — when a shared tile and a local tile occupy the same time slot, they appear side-by-side (same as two overlapping local sections)
- Co-scheduling (identical start+end) between a shared tile and a local tile: stacked within one card, separated by the entry separator rule. The shared entry uses purple text; the local entry uses normal styling.

**Frequency blocks:** Shared tiles with frequency annotations (odd/even/specific weeks) render exactly like local frequency-annotated tiles — the annotation appears as a small suffix.

### Shared Schedule Strip (Import Panel)

A **collapsible strip** between the filter bar and the schedule grid. Hidden when no shared schedules are loaded.

**Collapsed state (default when sources are loaded):**
```
┌─────────────────────────────────────────────────────────────────────┐
│ ▶ Shared Schedules: Chemistry Dept (12 sections) · Biology (8)  [Dismiss All] │
└─────────────────────────────────────────────────────────────────────┘
```
- Single-line summary: source labels with section counts, separated by ` · `
- `▶` chevron toggles expand/collapse
- "Dismiss All" button (trash icon, right-aligned) clears all loaded shared schedules
- Background: `SharedScheduleStripBackground`
- Left accent: 3px `SharedScheduleBorder` vertical rule
- Font: 11px, `SharedScheduleText` for source labels, standard text for "Shared Schedules:" label

**Expanded state:**
```
┌─────────────────────────────────────────────────────────────────────┐
│ ▼ Shared Schedules                                      [Dismiss All] │
│ ┌─ Chemistry Department ── exported 2026-05-16 ─── [×] ──────────┐ │
│ │  CHEM101 A   MWF 8:00–8:50 AM                                  │ │
│ │  CHEM201 B   TTh 10:00–11:20 AM   "Lab requires goggles"       │ │
│ │  CHEM310 A   M 1:00–2:50 PM (odd) / M 1:00–1:50 PM (even)     │ │
│ └─────────────────────────────────────────────────────────────────┘ │
│ ┌─ Biology Department ── exported 2026-05-14 ─── [×] ────────────┐ │
│ │  BIOL101 A   MWF 9:00–9:50 AM                                  │ │
│ │  BIOL240 C   TTh 1:00–2:20 PM                                  │ │
│ └─────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

**Per-source group:**
- Header: source label (bold) + export date (muted) + `[×]` dismiss button
- Section rows: `CourseCode SectionCode   schedule summary   "notes"` — read-only, no interaction
- Schedule summary format: `"MWF 8:00–8:50 AM"` (same format as section list schedule lines)
- Notes shown in italic/muted, truncated with ellipsis if long
- Each group is independently collapsible (sub-chevron)
- `[×]` dismiss removes that single source and its tiles from the grid

**Styling:**
- MaxHeight when expanded: 200px (scrollable if many sections)
- Section rows: FontSize 10, compact padding (2px vertical)
- Per-source border: 1px `SharedScheduleBorder` with CornerRadius 3
- The entire strip is not detachable (it's a lightweight info panel, not a primary workspace)

### Sharing Menu

A new top-level menu item: **Sharing**

```
Sharing
├── Import Shared Schedule…       (opens file dialog → loads CSV → tiles appear on grid)
├── Export Shared Schedule…       (exports current filter state to CSV → save dialog)
├── ─────────────────
│   Set Shared Folder…            (folder picker → persisted in AppSettings)
│   ✓ \\dept-share\schedules      (shows current path when set; click re-opens picker)
├── ─────────────────
    Dismiss All Shared Schedules  (visible only when shared schedules are loaded)
```

**Shared Folder Setting**
- A single configured folder used as the default starting location for both Import and Export file dialogs
- Persisted in `AppSettings` as `SharedScheduleFolder` (nullable string)
- "Set Shared Folder…" opens a folder-picker dialog; selected path is saved immediately
- When set, the current path is displayed as a disabled menu item below (✓ prefix, muted text, truncated with ellipsis if long)
- When not set, Import/Export file dialogs open to the OS default (usually Documents or last-used location)
- Typical value: a shared network drive like `\\dept-server\schedules\` or `S:\Scheduling\`
- WASM: "Set Shared Folder…" is hidden (`PlatformCapabilities.SupportsFileDialogs`)

**Import Shared Schedule…**
- Opens a file-open dialog filtered to `*.csv`, starting in `SharedScheduleFolder` if set
- On success: parses CSV, adds to SharedScheduleService, tiles appear on grid, strip becomes visible
- On parse error: inline error in the strip (not a modal dialog) — follows WASM-safe pattern

**Export Shared Schedule…**
- Opens a save-file dialog, starting in `SharedScheduleFolder` if set
- Default filename: `{InstitutionAbbreviation} Schedule {SemesterName}.csv`
  - Fallback if no institution abbreviation: `Schedule {SemesterName}.csv`
- Exports all currently-visible sections (respecting active filters) per Phase 2 spec
- Before export: prompt for source label (pre-filled with institution name if available)
- WASM: export button is hidden (`PlatformCapabilities.SupportsFileDialogs`)

**Dismiss All Shared Schedules**
- Clears all loaded shared schedules
- Visible/enabled only when `SharedScheduleService.HasAny` is true
- No confirmation dialog (data is transient; the CSV file still exists on disk)

### Filter Bar Badge

When shared schedules are loaded, a summary badge appears alongside the existing filter/overlay badges (in the summary row above the grid):

```
Filtered by: Instructor — Smith  |  Overlay: Room — SCI201  |  Shared: Chemistry (12), Biology (8)
```

- Uses `SharedScheduleBadgeText` (purple) color
- Format: `"Shared: {Label1} ({count}), {Label2} ({count})"`
- Clicking the badge text could toggle the strip expand/collapse (nice-to-have, not required for v1)

### Export Confirmation (Pre-Export Dialog)

Before writing the CSV, a lightweight inline prompt appears:

```
┌─────────────────────────────────────────────────────────────────┐
│ Export Shared Schedule                                          │
│                                                                 │
│ Source label: [Chemistry Department          ]                  │
│                                                                 │
│ 12 sections will be exported (matching current filters).        │
│                                                                 │
│                              [Cancel]  [Export]                  │
└─────────────────────────────────────────────────────────────────┘
```

- Source label text field: pre-filled with institution name if set, otherwise empty
- Section count: computed from current visible list
- This label becomes the `#TermPoint Schedule Overlay,...` header comment in the CSV
- Implemented as a small dialog (not a flyout — it's a one-shot confirmation, not a workspace)

### Semester Switch Behavior

Per Phase 1 decision: shared schedules are dismissed on semester switch.
- `SharedScheduleService.DismissAll()` called when the active semester changes
- The strip disappears; grid tiles removed
- No confirmation dialog — the data is transient and semester-specific
- If the user switches back to the original semester, they must re-import (the CSV file still exists)

### Keyboard Shortcuts

None for v1. Import/export are infrequent operations; menu access is sufficient.

### WASM Considerations

- **Import**: works via browser file picker (same pattern as other WASM file operations)
- **Export**: hidden if `PlatformCapabilities.SupportsFileDialogs` is false (WASM save not supported)
- **Strip/grid rendering**: fully functional in WASM — no platform-specific rendering
- **SharedScheduleService**: pure in-memory; no SQLite dependency

### Accessibility

- Strip is keyboard-navigable (Tab into strip, Enter to expand/collapse, Tab through dismiss buttons)
- Shared tiles have `AutomationProperties.Name` set to `"Shared: {CourseCode} {SectionCode}"`
- Color contrast: `#7C5CCC` on white background meets WCAG AA for normal text (4.62:1 ratio)

### What Phase 3 Does NOT Define
- CSV parsing error handling and edge cases (Phase 4 — Data)
- SharedScheduleService implementation, DI registration, event wiring (Phase 5 — Architecture)
- Unit test strategy (Phase 5 — Architecture)
- Exact AXAML markup (implementation detail)

---

## Phase 4 — Data
**Status: LOCKED** — signed off 2026-05-16

### CSV Parser

**Approach:** Hand-rolled line-by-line parser with RFC-4180 quoted-field handling. No external library (e.g., CsvHelper). The format is fixed and controlled by our own exporter; the only non-trivial case is Notes fields containing commas or newlines within quoted fields.

**Test coverage is critical** — the parser must have thorough unit tests covering all edge cases (quoted fields, embedded commas, embedded newlines, escaped quotes, empty fields, BOM handling, malformed rows).

### File Structure Expectations

1. **Line 1 (optional):** Header comment starting with `#`. Parsed for SourceLabel and export date. If absent or malformed, SourceLabel defaults to filename, date defaults to null. Never causes rejection.
2. **First non-comment line:** Column header row. Column names matched case-insensitively against the known set (`CourseCode,SectionCode,Notes,Day,StartTime,EndTime,DurationMin,StartMinutes,Frequency`). If the header doesn't match, the **entire file is rejected** — this catches wrong-file-type errors early.
3. **Remaining lines:** Data rows (up to 3,000 — see File Size Guard below).

### Encoding

- **Export:** UTF-8 with BOM (3-byte `EF BB BF` prefix). Ensures Excel on Windows displays accented characters correctly without a manual import wizard.
- **Import:** UTF-8 assumed, with or without BOM. If the file contains invalid UTF-8 sequences, reject the entire file (catches accidental .xlsx or binary file selection).

### File Size Guard

Hard reject if data rows (excluding header comment and column header) exceed **3,000 rows**. Error message: "File has {n} rows — maximum is 3,000. This may not be a shared schedule file."

Rationale: a real department export is unlikely to exceed ~600 rows. 3,000 provides generous headroom while catching wrong-file mistakes before the grid becomes unusable.

### Validation Rules Per Row

**Required fields** (row skipped if missing or unparseable):
| Field | Rule |
|-------|------|
| CourseCode | Non-empty string |
| SectionCode | Non-empty string |
| Day | Must resolve to 1–7 (see Day Parsing below) |
| StartMinutes | Integer 0–1439 |
| DurationMin | Integer > 0 |

**Exception — unscheduled sections:** If Day, StartMinutes, AND DurationMin are ALL blank/empty, the row is valid and produces a `SharedSection` with no meetings. This section appears in the strip listing (marked "(unscheduled)") but not on the grid. If only *some* time fields are blank (e.g., Day present but StartMinutes missing), the row is malformed and skipped.

**Optional fields** (blank is fine):
| Field | Behavior |
|-------|----------|
| StartTime | Ignored by parser (human-readable convenience column) |
| EndTime | Ignored by parser (human-readable convenience column) |
| Notes | Blank = no notes; carried to tooltip and strip display |
| Frequency | Blank = every week. If present, must be "odd", "even", or comma-separated integers. Invalid value treated as blank with a warning in the import summary. |

### Day Name Parsing

The exporter writes full English names. The parser defensively accepts common abbreviations (case-insensitive):

| Day | Accepted values |
|-----|-----------------|
| 1 (Monday) | Monday, Mon, Mo, M |
| 2 (Tuesday) | Tuesday, Tue, Tu, T |
| 3 (Wednesday) | Wednesday, Wed, We, W |
| 4 (Thursday) | Thursday, Thu, Th, R |
| 5 (Friday) | Friday, Fri, Fr, F |
| 6 (Saturday) | Saturday, Sat, Sa |
| 7 (Sunday) | Sunday, Sun, Su |

Note: Single-letter "S" is NOT accepted (ambiguous Saturday/Sunday). This flexibility is defensive — not documented or advertised to users.

### Error Handling

**Partial success model.** The importer skips malformed rows and imports the rest. After parsing:
- If all rows are valid: silent success, tiles appear on grid, strip becomes visible.
- If some rows are skipped: inline warning in the strip — e.g., "Imported 10 of 12 rows (2 skipped: lines 5, 9)". Tooltip or expanded detail shows per-line reason.
- If ALL data rows are invalid (or file has zero data rows): treated as a file-level error — "No valid sections found in file."

File-level rejections (entire file refused, nothing imported):
- Column header doesn't match known columns
- Invalid UTF-8 encoding
- Exceeds 3,000 data rows
- File is empty or contains only the header comment

### Row Grouping

Multiple CSV rows with the same **CourseCode + SectionCode** (case-insensitive) are grouped into a single `SharedSection`. Each row becomes one `SharedMeeting` on that section.

- Notes: taken from the first row encountered for each group. If subsequent rows have different Notes values (only possible via hand-editing), the discrepancy is silently ignored.
- Unscheduled rows (all time fields blank) for a CourseCode+SectionCode that also has scheduled rows: the unscheduled row is ignored (the section already has meetings).

### Duplicate Import Handling

**No deduplication.** Every import creates a new `SharedScheduleSet` regardless of SourceLabel, filename, or content. If the user imports the same file twice, they see two entries in the strip and double-rendered tiles on the grid. They dismiss the stale one manually via `[×]`.

Rationale: SourceLabel and filename are both freeform and sender-controlled. Attempting match/replace logic on unreliable keys adds complexity for minimal benefit given that dismissal is one click.

**Known limitation:** If the sender shares two overlapping exports (e.g., "Fall semester" and "Fall semester — Upper Level"), sections common to both files render twice on the grid. This is accepted for v1 — cross-file section deduplication would require matching without shared IDs, which is not worth the complexity for transient display data.

### Export Behavior

**Scope:** All sections currently visible in Section List (respecting active filters), including sections with zero meetings.

**Zero-meeting sections:** Emit one row with blank Day, StartTime, EndTime, DurationMin, StartMinutes columns. CourseCode, SectionCode, and Notes are populated normally.

**Row ordering:** Sections grouped by CourseCode+SectionCode (alphabetical), meetings within a section ordered by Day then StartMinutes.

**Notes repetition:** Section-level Notes value repeated on every row of that section (consistent with BackupService pattern).

**Header comment:** First line of output is `#TermPoint Schedule Overlay,{SourceLabel},{ISO date}` where SourceLabel comes from the pre-export prompt.

**Encoding:** UTF-8 with BOM.

**Default filename:** `{InstitutionAbbreviation} Schedule {SemesterName}.csv` (fallback: `Schedule {SemesterName}.csv`). User can rename freely in the save dialog.

### What Phase 4 Does NOT Define
- SharedScheduleService class design, DI registration, event wiring (Phase 5 — Architecture)
- Grid integration implementation details (Phase 5 — Architecture)
- Unit test class structure and test case list (Phase 5 — Architecture)
- AXAML markup for strip, tiles, or dialogs (implementation detail)

---

## Phase 5 — Architecture
**Status: LOCKED** — signed off 2026-05-16

### New Types

| Type | Location | Responsibility |
|------|----------|----------------|
| `SharedScheduleBlock` | `ViewModels/GridView/GridData.cs` | New `GridBlock` subtype for shared schedule meetings |
| `SharedScheduleService` | `Services/SharedScheduleService.cs` | Holds active `SharedScheduleSet` list; import/export/dismiss |
| `SharedScheduleCsvParser` | `Services/SharedScheduleCsvParser.cs` | Stateless CSV→`SharedScheduleSet` parser |
| `SharedScheduleCsvExporter` | `Services/SharedScheduleCsvExporter.cs` | Stateless sections→CSV writer |
| `SharedScheduleSet` | `Models/SharedScheduleSet.cs` | In-memory container (SourceLabel, ExportedAt, Sections) |
| `SharedSection` | `Models/SharedSection.cs` | In-memory section (CourseCode, SectionCode, Notes, Meetings) |
| `SharedMeeting` | `Models/SharedMeeting.cs` | In-memory meeting (Day, StartMinutes, DurationMinutes, Frequency) |
| `SharedScheduleStripViewModel` | `ViewModels/SharedScheduleStripViewModel.cs` | Drives the collapsible strip UI |
| `ImportResult` | `Services/SharedScheduleCsvParser.cs` | Nested record: `SharedScheduleSet`, skipped rows, warnings |

### SharedScheduleBlock (GridData.cs)

```csharp
/// <summary>
/// A meeting from an imported shared schedule CSV. Rendered with purple outlined styling.
/// Does not participate in selection or context menu interactions.
/// </summary>
public record SharedScheduleBlock(
    int Day, int StartMinutes, int EndMinutes,
    string Label, string FrequencyAnnotation = "",
    string SourceLabel = "",
    string Notes = "",
    string SemesterId = "", string SemesterName = "", string SemesterColor = ""
) : GridBlock(Day, StartMinutes, EndMinutes, IsOverlay: false, SemesterId, SemesterName, SemesterColor);
```

Design notes:
- `IsOverlay = false` — shared schedule tiles use their own rendering path, not the red overlay path
- `Label` = `"{CourseCode} {SectionCode}"` — pre-formatted at block creation time
- `SourceLabel` + `Notes` carried for tooltip construction
- No `SectionId` — shared blocks have no local database entity

### TileEntry Extension

Add `IsSharedSchedule` flag:

```csharp
public record TileEntry(
    ...,
    bool IsSharedSchedule = false);
```

`ToEntry` gains a new case:

```csharp
SharedScheduleBlock sh => new TileEntry(sh.Label, string.Empty, string.Empty,
    IsCommitment: true, IsSharedSchedule: true, FrequencyAnnotation: sh.FrequencyAnnotation),
```

`IsCommitment = true` reuses the existing click-suppression logic (no selection, no context menu, no hand cursor). `IsSharedSchedule = true` tells the renderer to apply purple styling instead of default/red.

### SharedScheduleService

```csharp
public class SharedScheduleService : ObservableObject
{
    private readonly List<SharedScheduleSet> _sets = new();

    /// <summary>All currently loaded shared schedule sets.</summary>
    public IReadOnlyList<SharedScheduleSet> Sets => _sets;

    /// <summary>True when at least one shared schedule is loaded.</summary>
    public bool HasAny => _sets.Count > 0;

    /// <summary>Fires when the set collection changes (import, dismiss, dismiss-all).</summary>
    public event Action? Changed;

    /// <summary>Adds a parsed set. Fires Changed.</summary>
    public void Add(SharedScheduleSet set) { _sets.Add(set); OnChanged(); }

    /// <summary>Removes a single set by reference. Fires Changed.</summary>
    public void Dismiss(SharedScheduleSet set) { _sets.Remove(set); OnChanged(); }

    /// <summary>Removes all sets. Fires Changed.</summary>
    public void DismissAll() { _sets.Clear(); OnChanged(); }

    /// <summary>
    /// Builds SharedScheduleBlocks for all loaded sets. Called by the grid VM
    /// during its block assembly pipeline.
    /// </summary>
    public List<SharedScheduleBlock> BuildBlocks(string semesterId, string semesterName, string semesterColor)
    {
        var blocks = new List<SharedScheduleBlock>();
        foreach (var set in _sets)
            foreach (var section in set.Sections)
                foreach (var mtg in section.Meetings)
                    blocks.Add(new SharedScheduleBlock(
                        mtg.Day, mtg.StartMinutes, mtg.EndMinutes,
                        $"{section.CourseCode} {section.SectionCode}",
                        FormatFrequency(mtg.Frequency),
                        set.SourceLabel,
                        section.Notes ?? "",
                        semesterId, semesterName, semesterColor));
        return blocks;
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(HasAny));
        Changed?.Invoke();
    }
}
```

Design notes:
- Inherits `ObservableObject` (MVVM Community Toolkit) for property change notifications
- `Changed` event is the lightweight signal that `ScheduleGridViewModel` subscribes to for grid reloads
- `BuildBlocks` takes semester context so blocks carry the correct semester routing in multi-semester mode
- Thread safety: UI-thread-only (same as all other VM-layer code in this app)

### DI Registration (App.axaml.cs)

```csharp
services.AddSingleton<SharedScheduleService>();
services.AddSingleton<SharedScheduleCsvParser>();
services.AddSingleton<SharedScheduleCsvExporter>();
```

All three are singletons:
- `SharedScheduleService` — holds state for app lifetime
- `SharedScheduleCsvParser` — stateless, singleton for convenience (avoids repeated allocation)
- `SharedScheduleCsvExporter` — stateless, same rationale

### Grid VM Integration

**Subscription** (constructor):
```csharp
_sharedScheduleService.Changed += Reload;
```

**Block injection** — new Pass 4 inserted after existing Pass 3 (commitments), before `ComputeTiles`:

```csharp
// Pass 4: Shared schedule blocks (cross-department CSV imports)
if (_sharedScheduleService.HasAny)
{
    foreach (var (semId, semName, semColor) in activeSemesters)
        combinedBlocks.AddRange(_sharedScheduleService.BuildBlocks(semId, semName, semColor));
}
```

Shared schedule blocks flow into the existing `ComputeTiles` pipeline unchanged — they participate in overlap detection and co-scheduling merge automatically because `ComputeTiles` operates on `GridBlock` base type only.

**Tooltip** — `BuildTileTooltip` gains a branch for `SharedScheduleBlock`:

```csharp
SharedScheduleBlock sh =>
    BuildSharedTooltip(sh)  // "{Label}\n{SourceLabel}\n{time range}\n{Notes if present}"
```

### Semester Switch Hook

`SharedScheduleService.DismissAll()` is called when the active semester changes. Wiring options (in order of preference):

**Option A — SemesterContext PropertyChanged** (preferred):
```csharp
// In MainWindowViewModel or wherever semester switch is orchestrated:
_semesterContext.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(SemesterContext.SelectedSemesters))
        _sharedScheduleService.DismissAll();
};
```

This is consistent with how other components react to semester switches. The `DismissAll` call fires `Changed`, which triggers the grid reload that clears the tiles.

### SharedScheduleCsvParser

```csharp
public class SharedScheduleCsvParser
{
    public ImportResult Parse(Stream stream, string fallbackSourceLabel) { ... }
}

public record ImportResult(
    SharedScheduleSet? Set,
    int TotalRows,
    int SkippedRows,
    List<(int LineNumber, string Reason)> Warnings,
    string? FileError);   // non-null = entire file rejected
```

- Stateless — all state captured in `ImportResult`
- Accepts `Stream` (works with both file dialog and WASM browser file picker)
- `fallbackSourceLabel` = filename sans extension, used when header comment is absent
- Implements all Phase 4 validation rules (header detection, column matching, row validation, row grouping, file size guard, encoding check)

### SharedScheduleCsvExporter

```csharp
public class SharedScheduleCsvExporter
{
    public void Export(Stream output, string sourceLabel, IReadOnlyList<Section> sections,
                      Func<string, string> courseCodeLookup) { ... }
}
```

- Writes UTF-8 with BOM
- `courseCodeLookup` resolves `Section.CourseId` → display code (same pattern used elsewhere)
- Writes header comment, column header, then one row per meeting per section
- Zero-meeting sections emit one row with blank time fields

### SharedScheduleStripViewModel

```csharp
public partial class SharedScheduleStripViewModel : ObservableObject
{
    private readonly SharedScheduleService _service;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private ObservableCollection<SharedScheduleSetViewModel> _sources = new();

    public bool IsVisible => _service.HasAny;
    public string CollapsedSummary => BuildSummary();  // "Chemistry Dept (12 sections) · Biology (8)"

    public IRelayCommand DismissAllCommand { get; }
    public IRelayCommand<SharedScheduleSetViewModel> DismissOneCommand { get; }
}
```

- Rebuilds `Sources` collection on `_service.Changed`
- Each `SharedScheduleSetViewModel` wraps a `SharedScheduleSet` for per-source display

### Import/Export Orchestration

Import and export commands live on `MainWindowViewModel` (consistent with existing File menu commands). They delegate to parser/exporter services and add results to `SharedScheduleService`.

```csharp
// Import (simplified)
async Task ImportSharedScheduleAsync()
{
    var file = await _dialog.OpenFileAsync("CSV files", "csv");
    if (file is null) return;

    await using var stream = file.OpenRead();
    var result = _csvParser.Parse(stream, Path.GetFileNameWithoutExtension(file.Name));

    if (result.FileError is not null) { /* show error in strip */ return; }
    _sharedScheduleService.Add(result.Set!);
    if (result.SkippedRows > 0) { /* show warning in strip */ }
}
```

### AppSettings Addition

```csharp
public string? SharedScheduleFolder { get; set; }
```

Persisted in `settings.json`. Used as initial directory for import/export file dialogs when non-null. Set via "Set Shared Folder…" menu item (folder picker).

### Test Strategy

**Unit test classes** (all in `SchedulingAssistant.Tests/`):

| Class | Covers |
|-------|--------|
| `SharedScheduleCsvParserTests` | Core parser: valid files, header comment, column matching, row validation, grouping, encoding, file-size guard, edge cases |
| `SharedScheduleCsvExporterTests` | Export output: header comment, row ordering, notes quoting, zero-meeting sections, BOM |
| `SharedScheduleServiceTests` | Add/Dismiss/DismissAll, Changed event firing, BuildBlocks output |

**Parser test cases** (minimum):
1. Well-formed file with header comment → correct SourceLabel, date, sections, meetings
2. File without header comment → SourceLabel defaults to fallback
3. Malformed header (wrong prefix) → treated as no header, not an error
4. Wrong column headers → FileError set, Set is null
5. Mixed valid/invalid rows → partial success, skipped rows reported
6. All rows invalid → FileError "No valid sections found"
7. Exceeds 3,000 rows → FileError with count
8. Quoted fields with commas → Notes parsed correctly
9. Quoted fields with embedded newlines → multi-line notes
10. Escaped quotes (`""`) → correct unescaping
11. BOM present → stripped transparently
12. Invalid UTF-8 → FileError
13. Empty file → FileError
14. Unscheduled section (all time fields blank) → SharedSection with no meetings
15. Partial time fields (Day present, StartMinutes missing) → row skipped
16. Frequency values: "odd", "even", "1,6,7", invalid → correct parsing + warning
17. Day name variants: full, abbreviated, single-letter → correct day int
18. Ambiguous "S" → row skipped (Day unresolvable)
19. Case-insensitive column matching ("coursecode" = "CourseCode")
20. Case-insensitive section grouping ("CHEM101 A" + "chem101 a" → one section)

**Exporter test cases** (minimum):
1. Standard sections with meetings → correct CSV output
2. Section with notes containing commas → quoted field
3. Section with notes containing newlines → quoted field with embedded newline
4. Zero-meeting section → one row with blank time fields
5. Source label in header comment
6. Row ordering: alphabetical by course+section, then day+start within
7. Output starts with UTF-8 BOM
8. Frequency annotation passes through

**Service test cases**:
1. Add fires Changed
2. Dismiss removes correct set, fires Changed
3. DismissAll clears all, fires Changed
4. BuildBlocks returns correct blocks for all sets
5. HasAny tracks state correctly

### Dependency Graph

```
MainWindowViewModel
├── SharedScheduleService (singleton)
├── SharedScheduleCsvParser (singleton)
├── SharedScheduleCsvExporter (singleton)
└── SharedScheduleStripViewModel
    └── SharedScheduleService (same singleton)

ScheduleGridViewModel
└── SharedScheduleService (same singleton)
    └── subscribes to Changed → triggers Reload

SemesterContext.PropertyChanged
└── SharedScheduleService.DismissAll()
```

### Exception Handling

**Swallow-and-report.** Both parser and exporter wrap their entire body in `try/catch(Exception)` and surface failures through their return types — never throw to callers.

- **Parser**: unexpected exceptions → `ImportResult.FileError = "Unable to read file."` (no crash, no modal dialog)
- **Exporter**: unexpected exceptions → return a failure string surfaced as `LastErrorMessage` on the orchestrating VM

Rationale: this is transient, non-destructive, low-stakes data. A crash over a CSV operation would be disproportionate. Debuggability is preserved via `AppLogger` — caught exceptions are logged at Error level before being swallowed.

### What Phase 5 Does NOT Define
- AXAML markup (implementation detail — follows existing patterns)
- Exact wording of error/warning messages (implementation detail)
- Animation/transition details for strip expand/collapse (implementation detail)
- Menu item AXAML and command binding (follows existing menu patterns)

---

## Implementation Status
**Status: COMPLETE** — implemented 2026-05-16, awaiting user testing

### What Was Built (Branch: `Sharing`)

All five phases implemented. 600 tests pass (33 new shared schedule tests).

**Files created:**
- `Models/SharedMeeting.cs`, `SharedSection.cs`, `SharedScheduleSet.cs`
- `Services/SharedScheduleService.cs`, `SharedScheduleCsvParser.cs`, `SharedScheduleCsvExporter.cs`
- `ViewModels/GridView/SharedScheduleStripViewModel.cs`
- `ViewModels/Management/SharingViewModel.cs`
- `Views/GridView/SharedScheduleStripView.axaml` + `.axaml.cs`
- `Views/Management/SharingView.axaml` + `.axaml.cs`
- `Tests/SharedScheduleCsvParserTests.cs`, `SharedScheduleCsvExporterTests.cs`, `SharedScheduleServiceTests.cs`

**Files modified:**
- `App.axaml.cs` — DI registrations, ScheduleGridViewModel factory updated
- `AppColors.axaml` — 6 purple color resources added
- `ViewModels/GridView/GridData.cs` — `SharedScheduleBlock` record, `IsSharedSchedule` on `TileEntry`
- `ViewModels/GridView/ScheduleGridViewModel.cs` — Pass 4 injection, `SharedScheduleStrip` property, `SharedScheduleBadge`, semester-switch dismiss
- `ViewModels/MainWindowViewModel.cs` — `NavigateToSharing` command
- `Views/MainView.axaml` — "Sharing" nav button, strip in PanelContent, badge in header
- `Views/GridView/ScheduleGridView.axaml.cs` — purple tile rendering, shared tooltip
- `Services/AppSettings.cs` — `SharedScheduleFolder` property

### Deviations from Spec

1. **Export source label** — spec called for a small pre-export dialog. Implemented as an editable TextBox in the Sharing flyout (always visible, pre-populated from institution name). Simpler UX, avoids adding a new dialog type.
2. **Sharing nav** — spec described a top-level "Sharing" menu. Implemented as a nav button opening a flyout (consistent with existing Export, Help nav patterns in the app).
3. **Strip sub-chevrons** — spec mentioned independently collapsible per-source groups. Not implemented (expand/collapse is at the strip level only). Can be added if user feedback warrants it.
4. **Accessibility** — `AutomationProperties.Name` on shared tiles not yet set. Can be added post-testing.
5. **Badge click-to-toggle** — spec noted as nice-to-have. Not implemented.
6. **Exporter return type** — spec showed `void`. Implemented as `string?` (null = success, non-null = error message) for swallow-and-report pattern consistency.
