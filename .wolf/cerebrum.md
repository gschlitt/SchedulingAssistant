# Cerebrum

> OpenWolf's learning memory. Updated automatically as the AI learns from interactions.
> Do not edit manually unless correcting an error.
> Last updated: 2026-03-24

## User Preferences

<!-- How the user likes things done. Code style, tools, patterns, communication. -->

- **Git branching is user-controlled.** Never auto-create a branch — not even when committing on the default branch. Only branch when the user explicitly asks. When committing/pushing on the default branch, commit there as asked (or ask first); don't branch silently. Commit/push only when explicitly requested.

## Key Learnings

- **Project:** SchedulingAssistant

## Do-Not-Repeat

<!-- Mistakes made and corrected. Each entry prevents the same mistake recurring. -->
<!-- Format: [YYYY-MM-DD] Description of what went wrong and what to do instead. -->

- [2026-06-03] **Don't auto-create git branches.** Branch creation is the user's call. When asked to commit on the default branch, do not branch first — commit there (or ask). Only branch on explicit request.
- [2026-05-21] **Tour authoring sessions: only edit tour files.** When writing tour content, only edit `TourContent.axaml` and `TourActionDefinitions.cs`. Never fix issues by editing underlying app code (ViewModels, Views, Services, Models, etc.). If the app code has a bug, note it and move on.
- [2026-05-21] **No hardcoded dimensions in ViewModels.** Pixel widths, heights, and other view concerns belong in AXAML resources or on the data model — not as VM constants. If a dimension varies by data (e.g. tour card width per step), put it on the data model with a sensible default.
- [2026-05-21] **Collapsed Avalonia controls have zero size.** `IsVisible=false` collapses the control — `OnSizeChanged` never fires, `Bounds` is zero. If you need a control to track size while hidden, seed the size from the parent when it becomes visible.
- [2026-05-21] **Keep it simple — avoid over-engineering.** Don't add CSS class toggling, dispatcher posts, or push-back patterns when reading a value directly from the data model works. The user values clean, minimal solutions.

- [2026-04-26] .NET 10 WASM: `dotnet.run()` exits the runtime after Main returns. Use `const runtime = await dotnet.create(); await runtime.runMain();` in index.html instead.
- [2026-04-26] .NET 10 WASM trimming: `TrimMode=partial` required for Avalonia — full trimming breaks reflection-based bindings and JSON serialization. Also set `JsonSerializerIsReflectionEnabledByDefault=true`.
- [2026-04-26] `TrimmerRootAssembly` must use the `AssemblyName` (e.g. `TermPoint`), not the project folder name (`SchedulingAssistant`).
- [2026-04-26] When adding DI registrations to `ConfigureDemoServices`, compare against desktop `ConfigureServices` — easy to miss services like `AppNotificationService`.

## Decision Log

<!-- Significant technical decisions with rationale. Why X was chosen over Y. -->

### [2026-03-28] Shared network DB strategy
- Strategy: copy D to local D' on checkout, edit locally, push back via atomic rename on explicit "Save to Network"
- Lock file on network drive = source of truth for write access (not a sidecar notification)
- Heartbeat every 30-60s keeps lock alive; timer gap detects wake-from-sleep
- Write-back: D' → D.tmp (hash-verified) → File.Move(overwrite:true) atomic rename
- Session timeout during sleep → discard D', switch to readonly, no rescue path (user error)
- D' is invisible to user; backup taken from D' before each network push
- See memory.md Session 2026-03-28 for full flow and vulnerability list
