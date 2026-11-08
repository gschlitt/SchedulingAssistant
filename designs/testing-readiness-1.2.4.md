# 1.2.4 testing readiness: what's left of section 8

Tracks the rest of section 8 ("Testing") of [`spec_version_1.2.4_summary.md`](spec_version_1.2.4_summary.md).
Read this at every handoff. Started 2026-11-07.

## Why section 8 exists

1. **Near term:** enough testing in place to carry out the other sections of the summary quickly. The full
   automation isn't needed for that: 8.7 (test timings) cuts the waiting, and 8.11 hands some hand tests to the
   harness.
2. **Medium term:** as much automation as possible, so regression testing is easy and therefore actually done.

## Status by item (as of 2026-11-07)

| Item | State | Left |
|---|---|---|
| 8.1–8.7 harness work, test timings | Done | 8.1's summary entry has no Done note (HANDOFF lists it as done); add one. |
| 8.8 scripted setup and checks (`tools/FieldTest`) | Done | Its note still says "VM parts not yet run"; they now are (2026-11-06). Update the note. Dialog-only outcomes are still checked by eye. |
| 8.9 VMs as test computers | TPVM1 works; paused VM = sleep; M5 passed | TPVM2 when a scenario needs a third computer; one real Surface sleep per release. |
| 8.10 automated two-machine rig | Not started | Goal 2; see below. |
| 8.11 trim the hand-test tier | Not started | Step 3 below. |
| 8.12 flaky tests | Partly done | Step 2 below. |

## Goal 1: the three steps left, in order

### Step 1: prove a 1.2.4 test-timings build on the rig (one session)

8.7 is built but has never run end to end. TPVM1 runs the 1.2.3.4 sideload, which is why the M5 run on 2026-11-06
took about 11 minutes of lock-timer waiting.

- [ ] Build a 1.2.4 sideload MSIX.
- [ ] `Install-TermPoint` on TPVM1 and on GregDeskTop. A lower version can't install over a higher one; uninstall
      first if needed (that deletes logs and kept changes).
- [ ] Set 8.7's environment variable so launches by `Start-TermPoint` (through explorer.exe in the VM's desktop
      session) inherit it.
- [ ] Check: "Test timings" in the title bar, the scale recorded in the lock file (`Get-TestDatabaseState` shows
      it), and the 8.7 guards (a ×3 computer and a normal one never misjudge each other's lock).
- [ ] Re-run M5 at ×3 and compare with the 1.2.3 run (summary 8.9 note).
- [ ] Retake TPVM1's `Ready` checkpoint (`-Replace`) with the 1.2.4 build installed.

Until this works, every field check of a 1.2.4 fix (F20, F17, F18, F23, 6.12) runs at full lock timings.

### Step 2: 8.12, the flaky tests

A full harness run takes about 38 minutes; a random red result costs a re-run and doubt about every real failure.

- [ ] The pause-proof test that fails 2–7% of the time (its wake-check settle waits 5 × 8 ms of real time).
- [ ] The fault-seam test that caught a background task from an earlier test.
- [ ] `WizardExistingDbJoinTests.Step1a_JoinExistingDb_DoesNotOpenOrChangeD` (`readerMode: True`), failed once on
      2026-11-05 (two temp paths instead of one); not looked at yet.

### Step 3: 8.11, written as the 1.2.4 release test list (about half a session)

- [ ] One short document: the hand tests that stay, and the ones the harness now covers (per summary item T5, M1 B,
      M2, M3, M9, M10, M12, S1 and S6 found nothing new in 1.2.3).
- [ ] Keep by hand only what can't be simulated: real SMB behaviour, on-screen messages, Avalonia and process
      shutdown, MSIX install and upgrade, a real 1.2.2 alongside, one real sleep, macOS, Store flights.

### Housekeeping

- [ ] Done note on 8.1; update 8.8's note.

### Not needed for goal 1

- 8.10 (goal 2).
- TPVM2, until a scenario needs a third computer.
- Scenario files written in advance: write each with the fix it checks (F20's with the F20 fix, and so on).

**Windows 11** is covered by TPVM1: S8 (the AutoCompleteBox crash test) can run there now, attended. A VM can't
stand in for touch or Modern Standby; hence one real Surface sleep per release.

## Goal 2: unattended regression runs (later)

What the rig can and can't do today: it controls everything around TermPoint (processes, VM freezes, lock files, a
colleague's edit, logs, database copies), but nothing inside TermPoint's window (add a section, Save, accept a
takeover, answer "Restore?").

**Count (2026-11-07), of M1–M11 and S1–S11 in `docs/testing/network-field-tests-1.2.3.md`:**

| | Scenarios | Count |
|---|---|---|
| Unsupervised today | M2 (C3, kill an idle writer), M9 (a colleague joins read-only) | 2 |
| Unsupervised with UI driving | M1, M2, M3, M4, M5, M6, M7, M9, M10, S1, S2, S3, S4, and M8 (TPVM2 kept on 1.2.2) | 14 |
| Partly (pickers and the wizard are brittle to drive) | M11, S6, S8 | 3 |
| Stay with Greg | S5 (not built), S7 (judging the UNC picker), S9 (cloud-sync account), S10 (visual), S11 (macOS) | 5 |

**Two routes; the 8.10 decision is which, or both:**
- **The spec's 8.10:** TermPoint's real checkout and lock services run as headless agents under `tools/NetworkProbe`,
  coordinated by a step file on the share. Fast and deterministic, but tests the services, not the app (no messages,
  no shutdown).
- **UI Automation of the real app** from the FieldTest console, in the VM's desktop session. No product behaviour
  change (at most `AutomationProperties.Name` on a few controls, an `.axaml` change). It can also read dialog text,
  which closes the "checked by eye" gaps. Network faults from the host: `Disconnect-VMNetworkAdapter` (black hole),
  bandwidth caps (slow); clumsy's command-line options are unconfirmed.

**Plan for the UI route, in steps with a stop rule:**
1. One session: a minimal UI-driving layer plus M5 unattended, compared with the attended M5. If Avalonia's UI
   Automation proves flaky, stop; the attended rig is unaffected.
2. Then one scenario at a time, in value order: M7, M6, M1, M4 (the lock and save paths where real bugs were found).
3. The rest stay attended or are retired (8.11).

**Guards against a pass that proves nothing:**
- Every run ends with `Get-TestSections`: the data outcome is checked, not just the logs.
- Each automated scenario is proved once against a known answer: beside an attended run, and once where it must fail.
- If a control or dialog can't be found, the result is "couldn't run", never PASS.

## Log

- 2026-11-06: TPVM1 `Ready` checkpoint; paused/saved VM clock steps at once (wake fires); FieldTest smoke test passed;
  M5 passed on 1.2.3 (commits `ad1143b`, `8b0a141`, `e02fa31`).
- 2026-11-07: this document.
