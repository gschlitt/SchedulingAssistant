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
- Multiple simultaneous overlays from different sources

### Open Questions for Phase 2
1. Should the CSV carry any property display names (department, tags, etc.) for human readability, even if they can't be used for structured filtering?
2. Should there be a lightweight header row or metadata (e.g., source department name, export date) so User A knows what they're looking at?
3. Does the overlay need to survive across semester switches, or is it dismissed when User A changes semesters?
