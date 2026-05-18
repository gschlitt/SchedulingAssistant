# AI-Assisted Data Import — Progressive Design Spec

## Phase 1: Problem Statement
**Status: LOCKED**

### The Problem

University administrators adopting TermPoint already have their scheduling data — sections, instructors, courses, rooms — stored in other formats. Most commonly these are Excel spreadsheets, but the exact layout varies wildly between departments and institutions. Column names differ, data is organized differently, some fields are merged or split across columns, and conventions for things like time formats or day abbreviations are inconsistent.

A deterministic "one-click import" is not feasible because:
- We cannot predict the source format
- There are too many variations to write parsers for
- Even within one institution, different departments may use different spreadsheet layouts
- Cleaning and reshaping messy real-world data is inherently judgment-heavy work

### The Insight

AI language models are exceptionally good at exactly this kind of task — reading a messy spreadsheet, understanding the user's intent, and reshaping data into a target schema. Every user likely already has access to an AI assistant (ChatGPT, Claude, Gemini, Copilot, etc.).

### The Approach

Rather than building an AI into TermPoint, we **equip the user to use their own AI agent** as a data-preparation step:

1. **Admin pre-configures their environment first.** Before importing entity data, the admin sets up — using existing Settings UI — Campuses, Scheduling Environment values (section types, tags, staff types, room types, resources, reserves), Block Patterns, Legal Start Times, and Section Code Patterns. This configuration becomes the matching context for the AI.
2. **TermPoint generates dynamic prompts as .txt files.** Each prompt includes the target CSV schema, the user's actual configured data (real section type names, instructor lists, room names, block patterns, legal start times, etc.), and examples. The user saves the .txt file and pastes its content into their AI assistant along with their source data.
3. **TermPoint validates and imports.** The user brings the resulting CSV back into TermPoint, which runs quality checks (schema conformance, referential integrity, duplicate detection) and reports problems before committing the import.

### Why This Works

- **Zero AI infrastructure cost** — no API keys, no cloud dependency, no model hosting
- **User controls their data** — nothing leaves TermPoint; the user decides what to share with their AI
- **Format-agnostic** — the AI handles whatever the source format happens to be
- **Trustworthy** — TermPoint validates everything before import; the AI is just a data-prep tool
- **Low maintenance** — if we change the schema, we update the prompt templates; no parser code to maintain per source format
- **Progressive** — users import one entity type at a time, in dependency order

### What "Done" Looks Like

A new user with their existing schedule data in Excel can:

1. Open an "Import" area in TermPoint
2. See a guided sequence of entity imports in dependency order
3. Choose the next entity type to import (e.g., "Instructors")
4. Click "Save Prompt" — TermPoint generates a .txt file containing the target schema, the user's live configuration data as matching context, and previously imported entity data for FK resolution
5. Open their AI assistant, paste the prompt + their source spreadsheet data
6. Get back a CSV, save it to disk
7. Load the CSV into TermPoint
8. See a validation report — errors, warnings, and a preview of what will be imported
9. Confirm, and the data is imported
10. Move to the next entity type; its prompt now includes the just-imported data

The full onboarding path should feel guided and achievable in a single sitting.

### Prerequisite: Admin Configuration (manual, before import)

These must be configured through existing Settings UI before beginning the import sequence. The import prompts will embed this data as matching targets for the AI:

- **Campuses** — names and abbreviations
- **Section Types** — Lecture, Lab, Tutorial, etc.
- **Tags** — Online, Hybrid, etc.
- **Staff Types** — Faculty, Sessional, TA, etc.
- **Room Types** — Lecture Hall, Lab, Seminar Room, etc.
- **Resources** — Projector, Whiteboard, etc.
- **Reserves** — Athletes, International, etc.
- **Block Patterns** — MWF, TTh, etc.
- **Legal Start Times** — per block length
- **Section Code Patterns** — naming rules with campus/type pre-fills

### Entity Import Order (dependency chain)

Each step's prompt includes the data from all preceding steps:

1. **Subjects** — standalone (e.g., BIOL, HIST, CHEM)
2. **Instructors** — references Staff Types
3. **Courses** — references Subjects, optionally Tags
4. **Rooms** — references Campuses, Room Types
5. **Sections** — references Courses, Instructors, Rooms, Campuses, Section Types, Tags, Resources, Reserves, Block Patterns, Legal Start Times

### Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Import granularity | One entity at a time | Simpler validation, clearer guidance, progressive |
| Prompt delivery | Save as .txt file | More reliable than clipboard for large prompts |
| Scheduling Env values | Manual setup first | User configures in Settings; prompt embeds as matching targets |
| Campuses | Manual setup only | Few enough to configure by hand |
| Prompt content | Dynamic with live data | Generated .txt includes real configured values for best AI matching |
| Time config in prompts | Include block patterns + legal start times | AI can validate/normalize meeting times |

### Open Questions for Phase 2

- What are the exact CSV column specs for each entity type?
- How should referential integrity errors surface in the validation report?
- Should validation allow partial import (skip bad rows) or require all-or-nothing?
- What does the Import area UX look like — wizard-style, flyout panel, dedicated view?
- How much of the existing SharedScheduleCsvParser can be reused vs. new parsing code?
- Should the prompt include a sample CSV the user can also hand to the AI as a concrete example?

---

*Phase 2: Domain Model — next session*
