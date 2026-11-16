---
name: start-work
description: Add a new piece of work to the release ledger (Release Ledger pattern), after a short interview.
disable-model-invocation: true
---

Add a new entry to the release ledger. The rules are in CLAUDE.md, "Release Ledger". Do these in order:

1. **Pull** (`git pull`). Read `release_state/README.md` and the current ledger. If either is missing, stop: no
   ledger is set up for this release yet, so ask Greg how to proceed.

2. **Interview Greg** briefly, asking only what his request and the docs don't already answer:
   - Which release (default: the current one), or the backlog.
   - The goal: what "done" means, in one or two sentences.
   - Design or fix: does it have (or need) a design doc under `designs/<release>/`? Features and substantial
     redesigns do; bug fixes and maintenance don't.
   - The first few steps, concrete enough to start.
   - Any open questions already known.

3. **Add the entry** to the ledger's **Work** list as **Not started**, in the ledger format (Goal · State · Steps ·
   Open questions · links). No detail beyond that format. If it is going to the backlog instead, add one line to
   `release_state/backlog.md` and stop after step 6.

4. **Create its folder** `release_state/<release>/<item-slug>/` with a NOTES.md holding the four collapsed sections
   (Files to read first · Decisions and why · Dead ends · Technical issues), a link back to the ledger, and a link to
   the design doc if any. If the work came from the backlog, remove its backlog line.

5. **Ask before making it ACTIVE** (rule 1). If Greg says yes, ask what state the current ACTIVE entry gets, and move
   the tag.

6. **Commit only the files this skill touched**, then push unless on master (rule 2: on master don't push or ask; remind Greg of the unpushed commits). Reply with the entry title,
   its state, and the commit hash.

Extra notes from Greg, if any: $ARGUMENTS
