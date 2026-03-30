# Cerebrum

> OpenWolf's learning memory. Updated automatically as the AI learns from interactions.
> Do not edit manually unless correcting an error.
> Last updated: 2026-03-24

## User Preferences

<!-- How the user likes things done. Code style, tools, patterns, communication. -->

## Key Learnings

- **Project:** SchedulingAssistant

## Do-Not-Repeat

<!-- Mistakes made and corrected. Each entry prevents the same mistake recurring. -->
<!-- Format: [YYYY-MM-DD] Description of what went wrong and what to do instead. -->

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
