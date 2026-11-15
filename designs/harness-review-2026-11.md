# Harness review after the M5 (4) miss (2026-11-12)

Why the harness didn't catch the "takeover, then release, while the writer sleeps" case that the 1.2.4 field test M5 (4)
found by hand, and what else has the same shape. Every recommendation is test or review work, except R2.

**Status (2026-11-12):** R1 done; **R2 done in 1.2.4 as "drop the notice"** (Greg: the notice gave the user nothing to
do); R9 partly done (the Log records the change; the M5 (4) expectation in the test list still to rewrite). The rest
waits for Greg's call (1.2.5 suggested).

## The miss, as a story

Bob is the writer with an unsaved change; his computer sleeps. Alice takes the stale lock over, changes nothing, and
closes TermPoint, which deletes the lock file. Bob's computer wakes to **no lock file at all**. TermPoint treats that as
"another program deleted it" (`CheckoutService.TryRecoverRemovedLockAsync`), puts the lock back and carries on as writer,
showing: "Another program removed the file … (antivirus or cloud-sync software can do this). TermPoint has put it back…".

- **Data: safe.** Every save checks that the shared database is exactly as Bob last saw it (rule 2). Proven on
  2026-11-12 by five new harness proof tests (`Harness/TakeoverThenReleaseProofTests.cs`): when Alice changed nothing,
  Bob's save lands; when Alice saved, Bob's save (button, autosave, or after the wake check) is refused (SourceModified),
  Alice's edit stays in the shared database and Bob's is kept.
- **The message: wrong.** It blames antivirus or cloud sync. Rule 4 ("never confuse the user").
- **The test list: wrong.** M5 (4) expected "Restore Unsaved Changes?", copied from the 1.2.3 document and never checked
  against 1.2.4's put-back behaviour.

## Why the harness didn't catch it

1. **Its checks can't fail this case.** The data invariants (I1-I9) all hold. The message checks judge promises about
   work ("kept", "saved", "restored"), plain words and one message per problem; they don't judge whether a message's
   stated cause is true. The put-back banner is filed as harmless information (`MessagePromises.cs`, "states a fact").
2. **One assumption, written three times.** The code comment ("the signature of antivirus, cloud-sync, or backup
   software … not of another user taking over"), the banner's wording, and the harness's classification all rest on
   the same belief, and the unit tests that cover a missing lock file (`SaveAsync_LockFileDeletedExternally_…`,
   `HeartbeatFailed_WhenLockFileDeleted_…`) simulate only antivirus, and pin the antivirus wording. Tests written from
   the designer's belief can't catch the belief being wrong.
3. **Nothing makes sure obvious stories are run.** The route takes 6 events (Bob opens, edits; time passes; Alice opens
   and takes over; Alice closes; Bob saves).
   - The exhaustive search goes 3 events deep (`HarnessBudget.DefaultDepth`).
   - 300 random walks of 30 events *can* reach it, but nothing records whether they did ("very likely" is not "known").
   - The pause sweep can't reach it at all: the random-walk `Sleep` sleeps and wakes in one step, and the pause action
     `TakesOver` / `TakesOverAndSaves` never closes Alice's TermPoint afterwards.
   - `KnownSequences` pins only routes that show a known finding; a route that must *pass* has no list of its own.
4. **Expected outcomes aren't stated per story.** A run passes if no rule is broken. "Bob is offered Restore" vs. "Bob's
   lock is put back" are both rule-abiding, so the harness has no opinion, and the field-test expectation drifted.

## The same shape elsewhere: places that guess a cause

Each of these turns an observation into a cause, and some say the cause to the user. For each, list every real way the
observation arises, check the behaviour is safe for all of them, and make the message neutral or hedged.

| Where | Observation | Assumed cause | Other real causes | Message says |
|---|---|---|---|---|
| `TryRecoverRemovedLockAsync` (wake, heartbeat, save) | lock file missing, no new holder | antivirus / sync deleted it | **another TermPoint took over and closed** (this miss); someone tidied the folder | was "Another program removed…" (wrong here); **since 2026-11-12 no message** |
| `LockLossPrompt.FileRemovedOrChanged` | lock file missing or unreadable, couldn't put back | another program | as above; a half-written file from a crash | "Another program removed or changed…" |
| `LocalCopyCheckPrompt` (open) | fresh copy locked | antivirus / backup scanning it | a second TermPoint on the same computer; indexer | "often antivirus or backup software" (hedged) |
| `RestorePrompt` | database file in use | antivirus, backup, sync | a colleague's open TermPoint; Excel export | "often antivirus, backup or sync software" (hedged) |
| `StaleLockPrompt` | heartbeat unchanged | asleep or disconnected | crashed; clock skew (item 6.12) | "may be asleep or disconnected" (hedged) |

The second states a cause as fact (the first did, until its notice was dropped). The others hedge ("often", "may be").

## Model gaps worth knowing

- **Two computers only** (`EventCatalogue.ComputerNames` = bob, alice). Three-computer stories (M5 (6), the double
  takeover; Carol) are field-test only.
- **Sleep is one step in the random walks.** Things that happen during a sleep come only from the pause sweep, whose
  actions are a fixed list (above: no "takes over and closes").
- **Exhaustive depth 3.** Any story longer than 3 events depends on random walks, which aren't measured.
- **Not modelled** (by design, so far): the ~85 s death of a stalled SMB operation (F22 stays known); share
  permissions (read-only share), a full share disk; a second TermPoint on the same computer (S1) — to check.

## Recommendations

| # | What | Cost | When |
|---|---|---|---|
| R1 | **Done:** named proof tests for takeover-then-release, five routes | — | 2026-11-12 |
| R2 | **Done in 1.2.4 (2026-11-12): the put-back notice is dropped.** Rewording was the first idea, but the notice told the user nothing they could act on ("carry on as normal"): the antivirus case needs no action, the "took over and closed without editing" case changed nothing, and the dangerous case (the other person saved) is told properly by the next save's conflict banner, so the notice was a second message for that problem. Now: the lock is put back silently, the log line and breadcrumb stay. `LockLossPrompt.LockFilePutBack` is gone; the two unit tests that pinned it now assert no message; the five proof tests assert it never appears. Display-only: no change to when or what TermPoint writes. **Still open:** `FileRemovedOrChanged` (the read-only banner when the lock couldn't be put back) names "another program" as the cause; that message is needed (the user is read-only and is told how to edit again), only its cause should go neutral (wording for Greg) | small | `FileRemovedOrChanged`: 1.2.5 |
| R2b | **Tell the dangerous case early (1.2.5, design + risk review):** when a missing lock is put back, check at once whether the shared database changed, and if it did, show the conflict banner then rather than at the next save. Wake and heartbeat path, so save-path rules apply | medium | 1.2.5 |
| R3 | Pause actions **"Alice takes over and closes"** and **"…saves and closes"**, so the pause sweep reaches the timing variants (e.g. Alice takes over and closes while Bob's wake check is held after it confirmed the lock was his) | small code; the pause sweep grows | next |
| R4 | **A must-cover story list:** every Alice-and-Bob story in the design docs and every field-test scenario (M1-M12, S1-S11) gets a named replay **with its expected outcome** (refused / restored / put back / which message), not just "no rule broken". Pinned in the harness like `KnownSequences`, but for routes that must pass | medium; mostly writing stories down | start in 1.2.5 |
| R5 | **Situation counters:** tag runs with named situations reached ("writer woke to a missing lock", "save refused after put-back", "takeover then release"…), report counts per harness run, and fail when a listed situation gets zero hits. Turns "very likely produced" into a number | medium | 1.2.5 |
| R6 | **Messages tell the truth about causes (new invariant I10):** the harness knows who deleted the lock file and why; a message that states a cause must match it. Start with `FileRemovedOrChanged` ("Another program removed or changed…") | medium | 1.2.5 |
| R7 | **Cause-guess audit** of the table above as a review step: for each, the other real causes, the behaviour for each, a test for each | small review | with R2 |
| R8 | **Design rule:** when a design decides "observation X means cause Y", the design doc lists every other way X arises and the test for each. Add to the persistence checklist beside the four rules | none | now (Greg) |
| R9 | Update the release test list: M5 (4)'s expectation (the lock is put back when the other person closed; Restore is offered only when they still hold it), and add the (4b) case (other person saves, then closes: Bob's save refused) | small | now |

## What the field tests caught that the harness couldn't

The person reading the banner. M4 and M5 so far passed every data check, as the harness predicted; the two real finds of
the day (this banner, and the expectation drift) are about what a person sees and what the test list says. That's an
argument for keeping one on-screen wording pass and a few real-sleep field tests per release (release-tests Part 3), and
for R4 + R6, which move part of that judgement into the harness.
