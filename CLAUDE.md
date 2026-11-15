# Coding Practice

Code should be clean. Prefer building reusable code, even if it takes more time, rather than repeating yourself.

Commenting: Methods should be commented, explain the purpose, its parameters, return values, and any exceptions it might throw.
Use /// XML documentation comments that a documentation generator can process.

**Design rationale for specific code lives in its XML doc comments** (e.g. the section editor's step-gate on
`SectionEditViewModel.AreOtherFieldsEnabled`, the Copy command, WASM settings in `TermPoint.Browser.csproj` and
`index.html`). Read them before changing that code, and put new rationale there rather than in this file.

Any fonts sizes, weights, and colors used should not be hardcoded, but refer back to appcolors.axaml or app.axaml

Strongly prefer MVVM style with minimal code-behinds.  Prefer attached behaviors to code-behinds, unless it is unnatural and too complex.

Keep solutions simple — read values directly from the data model rather than adding CSS-class toggling, dispatcher posts, or push-back patterns when a direct read works. Avoid over-engineering.

No hardcoded dimensions in ViewModels: pixel widths/heights and other view concerns belong in AXAML resources or on the data model — not as VM constants. If a dimension varies by data, put it on the data model with a sensible default.

# Avalonia Gotchas (cross-cutting)

- Collapsed controls (`IsVisible=false`) have zero size — `OnSizeChanged` never fires and `Bounds` is zero. If a control must track size while hidden, seed the size from the parent when it becomes visible.
- **Section list scrolling:** to show a section, set `SelectedItem` or call `ListScrollAnchorBehavior.ScrollItemIntoView(list, item)`. Never scroll the section ListBox's ScrollViewer directly (`Offset`, `ScrollIntoView`, `BringIntoView`) — scroll anchoring undoes it.
- **Class-conditioned styles are not free:** a style like `Run.warning` attaches a StyleInstance + activators to EVERY element of that type in the view, matching or not. Never use them for elements repeated on every section card — set values directly.
- **Dependency upgrades:** after Avalonia, run `dotnet run --project tools/ScrollAnchorProbe`; after Microsoft.Data.Sqlite / SQLitePCLRaw, run `dotnet test src/TermPoint.Tests/TermPoint.Tests.csproj --filter "FullyQualifiedName~CheckoutServiceUnchangedSaveTests|FullyQualifiedName~SqliteContentHashTests"`. (Why: comments beside those package references in `TermPoint.csproj`.)

# Persisted records
- Every class serialized into a `data` column (and every class nested inside one) must derive from `PersistedRecord`, so JSON fields written by newer app versions survive a round trip.
- Code that builds a fresh object to replace an existing record (editing copies, editor rebuilds of nested lists, clones) must call `WithExtensionDataFrom(original)` and copy every persisted field. Omitting either silently wipes data on save (this happened with section Flag/RoomTypeId and course Capacity/Level). `SectionCloneTests` and the `ExtensionData*Tests` enforce this.
- **`MinimumVersionToWrite`** is the oldest TermPoint allowed to edit a database (older versions open it read-only); it only ever goes up. For EVERY release ask: *"Could the new version save anything the old version wouldn't have?"* If Yes, set `ThisReleaseRequires`/`ThisReleaseReason` in `Services/MinimumVersionToWrite.cs`, update the test that pins them, and mark the Store submission mandatory. Never reset or lower them later. Full procedure: `UPDATE_NUMBERING_README.md`.

# Build & Test

- **Compile check** (fast verification after C#/AXAML edits — ~40s):
  `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`
  The `-f net10.0` is required: the project multi-targets (`net10.0;net10.0-browser`), and `-t:Compile` without `-f` fails with MSB4057. Compile-to-target avoids bin\Debug DLL locks held by Visual Studio.
  Run it again with `-f net10.0-browser` (the WASM demo build). If `--no-restore` Debug builds report unresolved Debug-only packages after a Release restore, run `dotnet restore src/TermPoint.Tests/TermPoint.Tests.csproj`.
- **Full test suite** (~80s, 900+ tests): `dotnet test src/TermPoint.Tests/TermPoint.Tests.csproj --nologo -v q`
  Scope to the area you changed when iterating: `--filter "FullyQualifiedName~CheckoutService"`
- **Full suite with the harness** (~40 min, 4000+ tests; required for persistence work):
  `TERMPOINT_HARNESS=1 dotnet test src/TermPoint.Tests/TermPoint.Tests.csproj --nologo -v q -o <out> --logger "trx;LogFileName=<out>.trx"`.
  Without `TERMPOINT_HARNESS` the harness tests skip. It spawns its own testhosts for `RandomWalkShardTests` (they are not strays). Always write a .trx on full runs.
- Tests marked `[FactRequiresLocalDb]` self-skip on machines without the private real-data DB (including CI) — skips are expected, not failures.
- If a full (non-Compile) build fails with MSB3026/MSB3027 (file locked), Visual Studio is holding the DLLs — build to a temp output instead: `-o $env:TEMP/sa-build`.
- **ALWAYS run the compile check after code edits, and the relevant tests after logic changes, before reporting work as done.**

# Version Control

The user controls when branches are created. Never create a git branch automatically — not even when committing on the default branch. Only create a branch when the user explicitly asks for one.

When asked to commit (or push) while on the default branch, do NOT branch first. Either commit on the current branch as instructed, or, if branching seems warranted, ask the user how they'd like to proceed. Commit and push only when explicitly asked.

# Working Agreements

@docs/dev/working-agreements.md

# Reference Notes (read when pertinent)

- `docs/dev/field-testing.md`: before a hand test or field test on the network share. Covers the test database and its reset, reading real logs (Claude's shell sees a virtualized `%APPDATA%`), faking sleeps and bad networks, MSIX installs.
- `docs/dev/dev-machines.md`: when a build, test run or script misbehaves, or before writing large output. Covers the full C: drive, `py -3.11`, helper scripts, process-kill rule.
- `HANDOFF.md`: the current state of work in progress, for picking up between sessions and machines.
- `WORKAROUNDS.md` (local, gitignored): Avalonia workarounds referenced from code comments.

# Debugging Practice

When debugging, when you generate more than one competing hypothesis, stop and propose the cheapest experiment that would distinguish between them before continuing.

When debugging, prefer one targeted probe over a complete theory - ask me to test first, synthesize later.

# Production Cautions
This app is now in production. Any future updates to code need to be carefully considered for potential impact on users. Analyze and foreground any risks, especially risks to data integrity. Advise on migration strategies
if appropriate.

## The four rules for the shared database

The shared database is a SQLite file on a network share, with no database server. The code that saves, locks, exits,
switches, restores and recovers it is held to four rules. Check every design that touches those paths against them
**before** laying it out, and say in the layout how it satisfies each one.

1. **The shared database only changes by one atomic rename of a copy that has been checked.**
2. **Never write it unless the lock is ours and the database is exactly as we last saw it.**
3. **Never delete the only copy of someone's unsaved work until a copy exists somewhere else.** The guarantee must
   come from the data layer (for example, a closed connection), never from a dialog that hides the controls: a modal
   blocks only the main window, not detached panels, sticky notes, focus-loss saves or background work.
4. **Never confuse the user.**
   - Every message says what happened, whether their work is safe, and what to do next.
   - Never ask about something the user has already dealt with.
   - One message per problem, in plain words: no "working copy" or "local copy", no paths, no log text.

# TermPoint — Project Decisions

## What This App Is
A scheduling **visualization and information management tool** for university administrators. It does not auto-schedule — it helps administrators see and manage how course sections fit together across a week.
Avalonia UI (C# / .NET), Windows and macOS, self-contained executable (no runtime or infrastructure for end users); a WASM demo build also exists.

## Data Storage
- **Database**: SQLite via `Microsoft.Data.Sqlite`, treated as a document store to avoid schema migrations.
- **Column pattern**: a stable set of key/partition columns plus a `data JSON` column for the entity's fields.
  Tables must be human-readable: include columns that identify the record in business terms, even if the app does not read them
  (e.g. `key TEXT, semestername TEXT, academicyearname TEXT, data JSON` with values like "Fall 2024-2025", "2024-2025").

## UI Design Principles
- **Conserve real estate.** Schedule grid tiles pack course code, section code and instructor initials on one line (e.g. "HIST101 A  JRS"); never a separate line for initials if avoidable. Co-scheduled sections (same start and duration) share one tile, stacked with a thin rule; overlapping sections sit side by side.
- **Section list is narrow and tall:** minimize padding, margins and font sizes. Editing expands the card inline (one open at a time, summary row stays visible); editor controls are compact (FontSize 10–11, tight padding).
- Manual data entry via forms only — no drag-and-drop scheduling.
