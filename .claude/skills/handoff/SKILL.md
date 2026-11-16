---
name: handoff
description: Wrap up this session so a fresh one can pick up exactly where we left off (Release Ledger pattern).
disable-model-invocation: true
---

Prepare to hand this work off to a new session. The rules are in CLAUDE.md, "Release Ledger". Do these in order:

1. **Update the ledger:** do everything in `.claude/skills/update-work/SKILL.md`. That brings the ACTIVE entry and
   its NOTES.md up to date and sends lasting lessons to CLAUDE.md or `docs/dev/`. Session progress never goes in
   CLAUDE.md.

2. **Check the state is honest:** the entry's State names the branch, anything broken or half-finished, and failing
   tests; the Steps say what to do next, in order; Open questions say what's waiting on Greg.

3. **Commit** the session's code and state docs on the current branch (no new branch). If something is unfinished,
   start the message with "unfinished:".

4. **Push** (rule 2). On master, ask Greg first: a master push redeploys the WASM demo.

5. **Propose a new session** titled with the ACTIVE entry's title, briefed only: "Read release_state/README.md;
   continue the ACTIVE entry."

6. Reply with the commit hash and a one-line summary.

Extra notes from Greg, if any: $ARGUMENTS
