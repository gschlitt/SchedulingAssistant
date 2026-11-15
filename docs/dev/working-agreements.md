# Working agreements

How Greg and Claude work together in this repo. Imported into `CLAUDE.md`, and tracked so it reaches every machine.

- **Designs as Alice-and-Bob stories:** Bob loses the lock or sleeps, Alice takes over, Carol is a third computer. Show
  today vs. with the fix first; technical detail after, compact.
- **Opus decides, Sonnet codes** (unless the job is short). Sonnet briefs say: no commits or branches, no .md/.axaml
  edits, keep CRLF, never `sed -i`, never `taskkill /IM`. Opus reviews the diff and runs both compile checks, the full
  suite and (for persistence work) the harness.
- **Fix-on-fix cascades:** before any change in the lock/save paths, check what else runs at the same moment (wake
  check, heartbeat, save, exit save, reader poll) and say how the tests cover it.
- **User-facing wording is approved by Greg before coding.** Comments marked "approved wording" guard such text.
- **Warn before editing any `.axaml` file:** VS 2022 won't reload an open one, so ask Greg to close its tab first.
- **Keep replies tight;** Greg is often low on tokens. Offer a handoff early in long sessions.
- **Lasting know-how doesn't go in `HANDOFF.md`.** That file holds only the state of work in progress. Put lessons that
  stay useful in `CLAUDE.md` or in a `docs/dev/` file.
