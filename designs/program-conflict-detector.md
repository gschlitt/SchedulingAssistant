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

## Phase 3: UX Sketch (early notes)

### Watch list

The user maintains a **watch list** of program-conflict watches. Each watch can be defined in one of two ways:

1. **Tag-based** — enroll an existing tag; all courses/sections carrying that tag are monitored
2. **Course-based** — manually specify a set of courses to monitor as a group

Each watch has an **on/off toggle** so the user can set up watches for several programs and activate only the ones they're currently working on.

### Schedule Grid indication

Because the schedule grid is canvas-drawn, the visual indication can be precise and spatial. Two cases arise naturally from the geometry:

- **Same card** (co-scheduled meetings from different courses in the same watch): sort the conflicting entries adjacent within the card and draw a colored box around them.
- **Adjacent cards** (overlapping but not co-scheduled): draw a colored box around each conflicting meeting and a connecting line between the two boxes.

### Color differentiation

The program-conflict indicator should use a **distinct hue** from room/instructor conflict indicators. Program conflicts represent a softer concern (student access) vs. a hard physical impossibility (double-booked room), and the color should reflect that difference.
