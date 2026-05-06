# anatomy.md

> Auto-maintained by OpenWolf. Last scanned: 2026-05-06T18:20:18.883Z
> Files: 20 tracked | Anatomy hits: 0 | Misses: 0

## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/.wolf/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Data/Repositories/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Models/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/ViewModels/Management/


## ../../../../../c/Users/gregs/source/repos/SchedulingAssistant/src/SchedulingAssistant/Views/Management/


## ../../../.claude/plans/


## ../../../.claude/projects/C--Users-gregs-source-repos-SchedulingAssistant/memory/


## ./

- `data-integrity-agenda.md` — Data Integrity & Concurrency Bug — Implementation Agenda (~8678 tok)

## .claude/


## .claude/rules/


## .claude/worktrees/dazzling-bardeen/


## .claude/worktrees/dazzling-bardeen/.claude/


## .claude/worktrees/dazzling-bardeen/docs/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Behaviors/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Controls/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Converters/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Data/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Data/Repositories/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Models/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Services/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/GridView/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/ViewModels/Management/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/GridView/


## .claude/worktrees/dazzling-bardeen/src/SchedulingAssistant/Views/Management/


## .claude/worktrees/interesting-wilbur/


## .claude/worktrees/interesting-wilbur/.claude/


## .claude/worktrees/interesting-wilbur/docs/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant.Tests/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Behaviors/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Controls/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Converters/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Data/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Data/Repositories/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Models/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Properties/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Services/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/GridView/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/ViewModels/Management/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/GridView/


## .claude/worktrees/interesting-wilbur/src/SchedulingAssistant/Views/Management/


## .github/workflows/


## .vs/SchedulingAssistant/DesignTimeBuild/


## .vs/SchedulingAssistant/config/


## .vs/SchedulingAssistant/v17/


## .vs/SchedulingAssistant/v17/TestStore/0/


## docs/


## src/AutoCompleteBoxRepro/


## src/FindPointToScreen/


## src/SchedulingAssistant.Anonymizer/


## src/SchedulingAssistant.Browser/


## src/SchedulingAssistant.Browser/wwwroot/


## src/SchedulingAssistant.Tests/

- `AppSettingsTests.cs` — Tests for F12 and F15 from the data-integrity audit (2026-05-04). <list type="bullet"> <item> <descr (~2646 tok)
- `BackupServiceTests.cs` — Tests for <see cref="BackupService"/>. <para>Tests in this file focus on the F1 fix from the data-in (~4853 tok)
- `CheckoutServiceTests.cs` — Integration tests for <see cref="CheckoutService"/>. <para>Each test uses an isolated temporary dire (~20518 tok)
- `EditorFlowTests.cs` — Integration-style unit tests for the inline editor workflows. Each test constructs the ViewModel und (~4891 tok)
- `MigrationGuardTests.cs` — Tests for F7 from the data-integrity audit (2026-05-04): the <c>SectionPropertyValues → SchedulingEn (~1983 tok)
- `RepositoryTransactionTests.cs` — Tests for F5 and F14 from the data-integrity audit (2026-05-04). <list type="bullet"> <item> <descri (~2247 tok)
- `SchemaVersionTests.cs` — Tests for F6 from the data-integrity audit (2026-05-04): <c>PRAGMA user_version</c> is written at th (~2445 tok)
- `WriteLockServiceTests.cs` — Unit tests for <see cref="WriteLockService"/>. <para>Each test fixture creates an isolated temporary (~7252 tok)

## src/SchedulingAssistant/

- `App.axaml.cs` — Logger available app-wide, including before DI is fully initialized. (~4210 tok)
- `MainWindow.axaml.cs` — Executes a save command if it exists and can currently execute. Returns true if the command was exec (~14569 tok)

## src/SchedulingAssistant/Assets/


## src/SchedulingAssistant/Behaviors/


## src/SchedulingAssistant/Controls/


## src/SchedulingAssistant/Converters/


## src/SchedulingAssistant/Data/

- `DatabaseContext.cs` — SQLite-backed implementation of <see cref="IDatabaseContext"/>. Opens the database file, creates the (~7007 tok)

## src/SchedulingAssistant/Data/Repositories/

- `ISectionRepository.cs` — Data access contract for <see cref="Section"/> entities (the core scheduling entity). (~692 tok)
- `SectionRepository.cs` — Returns all sections for the given course across all semesters, ordered by section code. (~2153 tok)

## src/SchedulingAssistant/Data/Repositories/Demo/

- `DemoSectionRepository.cs` — In-memory demo implementation of <see cref="ISectionRepository"/> backed by a mutable copy of <see c (~698 tok)

## src/SchedulingAssistant/Demo/


## src/SchedulingAssistant/Models/


## src/SchedulingAssistant/Services/

- `AppSettings.cs` — Persists app-level settings (e.g. database path) in a small JSON file in a stable AppData location t (~2796 tok)
- `BackupService.cs` — Manages automated SQLite backups and companion section CSV exports. <para><b>Backup file naming:</b> (~10280 tok)
- `CheckoutService.cs` — Manages the checkout / save lifecycle for every database the app opens. <para><b>Write-access mode:< (~19870 tok)
- `WriteLockService.cs` — Manages a file-based write lock that prevents two instances of the app from writing to the same SQLi (~8117 tok)

## src/SchedulingAssistant/ViewModels/


## src/SchedulingAssistant/ViewModels/GridView/


## src/SchedulingAssistant/ViewModels/Management/

- `CopySemesterViewModel.cs` — True when the current user holds the write lock; gates all Copy Semester controls. (~4525 tok)

## src/SchedulingAssistant/ViewModels/Wizard/


## src/SchedulingAssistant/ViewModels/Wizard/Steps/


## src/SchedulingAssistant/Views/


## src/SchedulingAssistant/Views/GridView/


## src/SchedulingAssistant/Views/Management/


## src/SchedulingAssistant/Views/Wizard/


## src/SchedulingAssistant/Views/Wizard/Steps/


## src/SchedulingAssistant/bin/Debug/net8.0/


## src/SchedulingAssistant/bin/Debug/net8.0/Help/


## src/SchedulingAssistant/bin/Release/net8.0-browser/


## src/SchedulingAssistant/bin/Release/net8.0/


## src/SchedulingAssistant/bin/Release/net8.0/Help/

