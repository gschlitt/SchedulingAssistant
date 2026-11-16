# The Release Ledger development pattern (quick reference)

All work in progress is tracked in this folder, not in session history or Claude's memory. Each release has a
**ledger** (`<release>/RELEASE_<release>.md`): one collapsed entry per piece of work, showing title · state · ACTIVE.
Open an entry for its goal, state, steps and open questions; follow its link to the **item notes**
(`<item-slug>/NOTES.md`) for decisions, dead ends, files to read first and technical issues.

**Rule of the levels:** each level only summarises the one below and links to it. Detail never moves up.

**Skills:** `/start-work` adds an entry · `/update-work` brings the ACTIVE entry up to date · `/handoff` updates,
commits, pushes and starts a fresh session. The full rules Claude follows are in CLAUDE.md, "Release Ledger".

## 1. The work loop

```mermaid
flowchart TD
    A([Idea, bug, or a design ready to build]) --> B["<b>/start-work</b><br/>interview: release, goal, design or fix, first steps"]
    B --> C["Entry added to the ledger: <b>Not started</b><br/>its folder and NOTES.md created"]
    C --> D{"Greg OKs it as <b>ACTIVE</b>?<br/>(the previous entry gets a state)"}
    D -- yes --> S["<b>Session</b><br/>1. read README → ledger → ACTIVE entry → NOTES.md<br/>2. work<br/>3. <b>/update-work</b> at each milestone"]
    S --> E{How does the session end?}
    E -- "more to do" --> HO["<b>/handoff</b><br/>update-work → commit (unfinished: …) → push*<br/>→ new session titled with the entry"]
    HO --> S
    E -- "needs a decision" --> G["<b>Waiting on Greg</b><br/>the open question shows in the ledger"]
    G -- "Greg answers" --> S
    E -- "code and tests done" --> NT["<b>Needs testing</b><br/>field test; MinimumVersionToWrite question"]
    NT -- "passes" --> DN["<b>Done</b> → moves to Closed<br/>one outcome line; NOTES.md stays"]
    NT -- "fails" --> S
    DN --> D
```

\* Pull before every skill. Off master, push after; on master the skill doesn't push or ask (a push redeploys the WASM demo) but reminds Greg of unpushed commits.

## 2. The states

```mermaid
flowchart LR
    NS[Not started] --> IP[In progress]
    IP <--> WG[Waiting on Greg]
    IP --> NT[Needs testing]
    NT --> DN[Done]
    NT -- test fails --> IP
    NS -.-> DF[Deferred → backlog or later release]
    IP -.-> DF
    NS -.-> DR[Dropped]
    IP -.-> DR
```

Exactly one entry is **ACTIVE** at a time; it changes only with Greg's OK.

## 3. The map: where things live

```mermaid
flowchart TD
    R["release_state/README.md<br/>current release (one line)"] --> RD["<b>The ledger</b>: RELEASE_&lt;release&gt;.md<br/>one collapsed line per entry: title · state · ACTIVE<br/>goal · state ≤3 lines · steps · open questions"]
    RD --> N["<b>Item notes</b>: &lt;item-slug&gt;/NOTES.md<br/>decisions and why · dead ends<br/>files to read first · technical issues"]
    RD -. links to .-> DS["designs/&lt;release&gt;/*.md<br/>what to build (features only)"]
    N -. links to .-> DS
    BL["release_state/backlog.md<br/>unassigned work"] -- /start-work --> RD
    CL["CLAUDE.md + docs/dev/<br/>lasting rules and know-how"]
    MEM["Claude's memory<br/>how Greg likes to work (never state)"]
```

Viewing: GitHub renders these diagrams; VS Code needs the "Markdown Preview Mermaid Support" extension.
