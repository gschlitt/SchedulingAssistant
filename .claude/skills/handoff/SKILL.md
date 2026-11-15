---
name: handoff
description: Wrap up this session so a fresh one can pick up exactly where we left off.
disable-model-invocation: true
---

Prepare to hand this work off to a new session. Do these in order:

1. **Update memory.** Add to CLAUDE.md only things that will still matter
   in future sessions: conventions, gotchas, decisions and why. Don't put
   session progress here; that belongs in HANDOFF.md.

2. **Write HANDOFF.md** at the repo root, replacing any earlier one:
   - Goal: what we're working toward overall
   - Done this session
   - Current state: branch, anything broken or half-finished, failing tests
   - Next steps, in order
   - Open questions or decisions waiting on me
   - Files to read first
   - Approaches tried that didn't work, and why

   Write it for a reader with no memory of this conversation.

3. **Commit** everything, including HANDOFF.md. If something is
   unfinished, say so in the commit message.

4. Reply with the commit hash and a one-line summary.

Extra notes from me, if any: $ARGUMENTS
