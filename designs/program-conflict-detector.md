# Program Conflict Detector — Progressive Design Spec

## Phase 1: Problem Statement

### The problem

Room and instructor conflicts are already detected: if two sections share a room or instructor and overlap in time, the app nudges the user. But the other major timetabling concern — **student access** — has no detection.

A student enrolled in a program needs to take specific courses. If sections of two required courses overlap in time, some students may be unable to build a workable schedule. Today, the user has to spot these overlaps manually.

### What the feature does

The user tags courses/sections with a label representing a program (e.g. "CS-Core"). They then enroll that tag in the program-conflict detector. Once enrolled, the application flags any two sections that:

1. Belong to **different courses** within the same enrolled tag, and
2. **Overlap in time**

The indication is mild — a nudge, not a blocker. The user may have good reasons for the overlap and can simply ignore it. There is no mute/dismiss mechanism in this iteration.

### What "done" looks like

- The user can enroll an existing tag in the program-conflict detector
- When two sections from different courses share an enrolled tag and overlap in time, a visual indication appears
- The indication is consistent in tone with existing room/instructor conflict nudges
- No new entities beyond the enrollment flag — programs are just tags
- No combinatorial analysis, slate-building, or schedule-feasibility checks

### Out of scope (this iteration)

- Muting or dismissing individual conflicts
- "All sections of C1 conflict with all sections of C2" aggregation
- Feasible-slate enumeration or counting
- Any notion of required vs. elective courses within a program

---

## Phase 2: Domain Model

### Entities

**Watch** (new)
- The core new entity. Represents a monitored group of courses whose sections should be checked for time overlaps.
- **Name**: auto-generated from the tag(s) or course list on creation; user-editable.
- **Scope**: per-semester. A watch belongs to a single semester.
- **Enabled**: boolean on/off toggle. Disabled watches are retained but produce no conflict indications.
- **Definition**: one of two modes:
  - *Tag-based*: one or more tags. A section is included if it carries **all** specified tags (AND logic). Example: tags "upper-level" + "BSc" matches only sections tagged with both.
  - *Course-based*: an explicit list of courses.

**Tag** (existing)
- Already in the system. Watches reference tags by identity, not by snapshot — if a section gains or loses a tag, the watch's coverage updates automatically.

**Course** (existing)
- Already in the system. In course-based watches, courses are referenced directly.

**Section** (existing)
- Belongs to a course. Has meeting times (day + time span). A section may appear in multiple watches (via multiple tags or explicit inclusion).

**Meeting** (existing)
- The day/time component of a section. Overlap detection operates at this level.

### Relationships

- A **Watch** → references one or more **Tags** (tag-based mode) or one or more **Courses** (course-based mode)
- A **Watch** → belongs to one **Semester**
- A **Section** → belongs to one **Course**, carries zero or more **Tags**, has one or more **Meetings**
- A **Section** can be covered by multiple watches (no exclusivity)

### Conflict (computed, not stored)

A program conflict is not a persisted entity. It is computed on the fly whenever the grid renders. A conflict exists when:

1. Two sections belong to **different courses**
2. Both sections are covered by the **same enabled watch**
3. At least one meeting of each section **overlaps in time** (same day, overlapping time span)

A single pair of sections may produce conflicts under multiple watches — each watch's indication is independent.

### Invariants

- A watch must reference at least one tag or one course (cannot be empty)
- Tag-based watches use AND logic: a section must carry *all* specified tags to be included
- Conflicts are only between sections of *different* courses — two sections of the same course sharing a time slot is not a program conflict (that's just multiple offerings)
- Disabling a watch suppresses all its conflict indications without deleting the watch or its definition

---

## Phase 3: UX Sketch

### Location: the Access group

The watch list lives in **Schedule View**, in a new toolbar group called **"Access"** — alongside the existing Filter and Overlay groups. This group is the future home for tools that help the user assess student access to the schedule. For now, its only content is the program-conflict watch list.

### Watch list control

The watch list is a **pulldown** (collapsed by default) within the Access group. When collapsed, it shows a **summary badge** — a compact text indicator of the current state, e.g. "2 watches, 3 conflicts" or "No active watches." This gives the user at-a-glance awareness without opening the pulldown.

When expanded, the pulldown shows:

- A list of all watches for the current semester
- Each watch displays:
  - Its **name** (auto-generated, editable)
  - Its **definition** (tag list or course list, shown compactly)
  - An **on/off toggle**
  - A **conflict count** for that watch (when enabled)
  - A **delete** action
- A button/link to **create a new watch**

### Watch creation flow

1. The user clicks "New Watch" in the pulldown
2. Chooses **mode**: tag-based or course-based
3. **Tag-based**: a dropdown/combobox to select tags (multi-select; AND logic). As tags are selected, a name is auto-generated (e.g. "upper-level + BSc")
4. **Course-based**: a multi-select of courses in the current semester. Name auto-generated from the course list (e.g. "BIOL101, CHEM101, PHYS101")
5. The user can **edit the name** to something more meaningful (e.g. "BSc Year 1 Core")
6. The watch is created in the **enabled** state

### Schedule Grid indication

Grid-only — no indication in the Section List view.

Because the schedule grid is canvas-drawn, the visual indication can be precise and spatial. Two cases arise naturally from the geometry of conflicting meetings:

**Same card** (co-scheduled meetings from different courses in the same watch):
- Sort the conflicting entries adjacent within the card's content list
- Draw a colored box around the conflicting pair

**Adjacent cards** (overlapping but not co-scheduled):
- Draw a colored box around each conflicting meeting
- Draw a connecting line between the two boxes, making the relationship explicit

### Color differentiation

The program-conflict indicator uses a **distinct hue** from room/instructor conflict indicators. Program conflicts represent a softer concern (student access) vs. a hard physical impossibility (double-booked room), and the color should reflect that — something calm and informational rather than alarming.

### Non-interactions

- No indication in the Section List view
- No click-through from the summary badge to specific conflicts on the grid (this iteration)
- No mute/dismiss on individual conflicts
- No tooltip or detail popup on the grid indication
