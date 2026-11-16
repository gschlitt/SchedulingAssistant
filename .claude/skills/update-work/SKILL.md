---
name: update-work
description: Bring the ACTIVE ledger entry and its item notes up to date with this session (Release Ledger pattern).
disable-model-invocation: true
---

Update the release ledger from this session. The rules are in CLAUDE.md, "Release Ledger". Do these in order:

1. **Pull** (`git pull`) if nothing in the working tree would conflict; otherwise skip and say so. Read
   `release_state/README.md`, the ledger, and the ACTIVE entry's NOTES.md.

2. **Rewrite the ACTIVE entry** (rewrite, don't append):
   - **State:** where it stands now, at most 3 lines.
   - **Steps (N left):** remove finished steps, add new ones, keep them in order and one line each.
   - **Open questions:** one line each. If work can't go on without Greg's answer, set the state to
     **Waiting on Greg**. If it can only go on once something outside our control arrives (another project's fix or
     release, a reply, a date), set **Waiting on others** and say in **State** what it waits for and how and when to
     check. Set other state changes too (In progress, Needs testing, Done).
   - **Done:** move the entry to **Closed**, collapsed to goal + one outcome line (date, commit). Before marking it
     Done, ask the `MinimumVersionToWrite` question if it touches what gets saved.

3. **Update its NOTES.md:** add decisions (with why), dead ends (with why they failed), files to read first, and
   technical issues found this session. Correct anything that is now wrong. Write for a reader with no memory of this
   conversation.

4. **Enforce the levels:** no code, paths, log text or reasoning in the ledger; nothing in NOTES.md that copies the
   spec. Move detail down, never up. Check that every link still resolves.

5. **Send lasting lessons out:** conventions, gotchas and know-how that will matter after this work is done go to
   CLAUDE.md or a `docs/dev/` file, not to NOTES.md.

6. **Don't commit** (that's `/handoff`'s job), unless Greg asks. Reply with the entry's new state line and one line on
   what changed.

Extra notes from Greg, if any: $ARGUMENTS
