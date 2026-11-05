# Handoff: TermPoint 1.2.4 in progress on branch `Version_1.2.4` (dev machine, 2026-11-03)

Written so a fresh conversation can pick up. Claude Code's memory and `CLAUDE.md` don't travel between machines
(`CLAUDE.md` is gitignored), so everything a new agent needs is here or in the files this points to.

**To start the new agent, say:** "Read `HANDOFF.md`, then let's continue 1.2.4."

## Where things are

- **Branches:** all 1.2.4 work is on **`Version_1.2.4`**, pushed to origin (tracking set) at `b2ec398` + this file.
  **`master`** = released 1.2.3 code (`cf4d4cb`) plus doc commits, kept hotfixable; it is 7 doc-only commits ahead of
  origin and **not pushed** (pushing `master` redeploys the WASM demo; pushing a `v*` tag starts the macOS workflow).
  Greg controls branches, commits and pushes: never create a branch, ask before each commit and each push.
- **1.2.3 is released on the Microsoft Store** (Store only), built from `cf4d4cb`. `MinimumVersionToWrite`: No for
  1.2.3 and (so far) No for 1.2.4.
- **The plan:** `designs/spec_version_1.2.4_summary.md` (progressive-reveal summary of `designs/spec_version_1.2.4.md`;
  every finished item has a "Done" note). Greg chose to do group 8 (testing) first.
- **Colour by subject (summary 2.3):** implemented through task 4 plus Revision 1 (markers, not chips), `c33861f`.
  Left: Greg's by-eye pass on real data; palette kept as is (summary 2.3's Done note). Spec `designs/subject-colours.md`.
- **Other open specs:** `designs/slot-browser.md` (Phase 1, awaiting Greg's lock), `designs/access-analyzer.md` (Phase 1, Q1–Q12 open).

## Done in 1.2.4 so far

- **Group 8 (testing):** 8.1 wake check + heartbeat together; 8.2 hang mode; 8.3 another program holding a share file;
  8.4 one clock per computer; 8.5 invariant I9 (rewrite only your own lock file); 8.6 message checks (I7 plain words,
  I8 one message per problem); 8.7 test-only lock timer scale; 8.12 partly (two flaky tests remain).
- **F21 data loss (3.12, `8143c5a`):** `CheckoutService.CleanupWorkingCopy` refuses to delete a working copy that still
  has its dirty marker. Also closes F2's close-time route.
- **4.12 F19 + 4.14 F22 (`cf2bce9`, 2026-11-03):**
  - a lock-file check counts for 2 s (`WriteLockService.LockCheckFreshness`, not scaled; steady and wall clock): it is
    repeated before the heartbeat's temp write and move, the release's delete, and both takeover deletes;
  - takeover confirmation 70 → **170 s** (`StaleConfirmationSeconds` = 60 + 10 + 85 + 15); the wait dialog says
    "(up to 3 minutes)" from 90 s on.
  - Basis: **experiment E1** (NetworkProbe `--late-landing`, GregDeskTop → Mekler): an abandoned SMB delete/rename lands
    when the network returns if the outage is ≤ 60 s, otherwise dies ~85 s after it was issued (default SessTimeout 60 s
    assumed). Report `tmp/netprobe/netprobe-20261103-093522.md` (local only).
  - F19 retired to `FixedFindingsProofTests`; F22 stays known because the harness doesn't model the 85 s limit.

## Next steps

1. **Rest of group 8:**
   - **8.8 built and 8.9 started (2026-11-03), not committed:** `tools/FieldTest/`. Read its `README.md` first.
     - The module drives every test computer from one elevated console.
     - `New-TestComputer.ps1` makes the VMs.
     - Greg's machine list is `tools/FieldTest/fieldtest.config.psd1`: GregDeskTop, TPVM1 and TPVM2, plus GregSurface
       switched off. It is gitignored.
   - **Next, by Greg:**
     - Download the Windows 11 ISO to E:.
     - Run `New-TestComputer.ps1 -Name TPVM1 -IsoPath ...`, elevated.
       - The VM starts on Hyper-V's Default Switch (NAT, now the script's default), so the host network is untouched.
       - If the VM can't open `\\mekler` by name, use `\\192.168.1.81` (Mekler's address on 2026-11-03), or move the
         VM to an external switch (README, "Network").
     - Install Windows, then `Install-TermPoint`, then the `Ready` checkpoint.
   - **Then:**
     - The first live run of the VM commands: Install, Start, Stop and Suspend-TestComputer. Nothing VM-side has run yet.
     - Note whether a paused VM's clock jumps on resume; the wake check needs the jump.
     - Then 8.10/8.11, and finish 8.12 (two flaky tests).
2. **Open findings (summary items):** 4.13 F20 (own leftover lock after a switch → read-only on return); 6.10 F17
   (two messages for one save problem); 6.11 F18 (two lock-lost banners); 6.12 false "Inactive" prompt when the
   writer's clock is behind; 6.13 F23 (kept-changes banner outlives a Discard).
3. **Left open by 4.12/4.14** (listed in the 4.12 Done note): teach the harness the ~85 s death of a stalled operation
   (then F22 can retire); test-timing builds scale the 170 s but not the 85 s; maybe record each writer's SessTimeout
   in the lock file, probe SessTimeout=30 and macOS; place the save's dirty-marker write that sits between its final
   check and the rename.
4. **Known findings now** (`src/TermPoint.Tests/Harness/KnownFindings.cs`): F2, F5, F6, F7, F16, F17, F18, F20, F22,
   F23, A13.
5. **Still open from the 1.2.3 release (check status with Greg):** Microsoft support case for the Store-update failure
   ("won't open until restart"; evidence in `designs/spec_version_1.2.4.md`, "Release and distribution", Process
   Monitor export `C:\temp\temp.CSV`); uninstall the sideload TermPoint and delete the test MSIX folders
   (`\\mekler\Users\gregs\temp\msix-1.2.2\` … `msix-1.2.3.4\`, `C:\temp\termpoint-msix-1.2.3.4`); N4 (Store update
   link); drop `Microsoft.DiaSymReader.Native.amd64.dll` from the package; package flights; failing GitHub Tests workflow.

## Build and test

- **Compile checks:** `dotnet build src/TermPoint/TermPoint.csproj -f net10.0 -t:Compile --no-restore -v q`, and the
  same with `-f net10.0-browser`. (A Release restore leaves Debug-only packages unresolved for `--no-restore` Debug
  builds; `dotnet restore src/TermPoint.Tests/TermPoint.Tests.csproj` fixes it.)
- **Full suite with the harness** (~38 min since the 170 s change; last run 4182 passed, 0 failed, 3 skipped):
  `TERMPOINT_HARNESS=1 dotnet test src/TermPoint.Tests/TermPoint.Tests.csproj --nologo -v q -o E:/sa-verify --logger "trx;LogFileName=E:/sa-verify.trx"`.
  It spawns its own testhosts for `RandomWalkShardTests` (not strays). Without `TERMPOINT_HARNESS` the harness tests skip.
- **C: is nearly full:** build and test output on drive E; delete with `cd /e && rm -rf ./name` (the absolute
  `rm -rf E:/...` form is blocked by a safety check).
- **NetworkProbe** (`tools/NetworkProbe`, README has every mode incl. `--late-landing`): build to `E:\netprobe-build`.

## Working agreements (Greg's)

- **Designs as Alice-and-Bob stories** (Bob loses the lock / sleeps, Alice takes over, Carol is a third computer),
  today vs. with the fix; technical detail after, compact.
- **Opus decides, Sonnet codes** unless short. Sonnet briefs: no commits/branches, no .md/.axaml edits, keep CRLF, never
  `sed -i`, never `taskkill /IM`. Opus reviews the diff, runs both compile checks, the full suite and (for persistence
  work) the harness; commit and push only when Greg asks.
- **Fix-on-fix cascades:** before any change in the lock/save paths, check what else runs at the same moment (wake
  check, heartbeat, save, exit save, reader poll) and say how the tests cover it. Check every persistence design
  against the four rules in `CLAUDE.md`; ask the `MinimumVersionToWrite` question every release.
- **User-facing wording is approved by Greg** before coding. **Warn before editing any `.axaml` file.**
- **Debugging:** with competing hypotheses, propose the cheapest probe first.
- **Keep replies tight;** Greg is often low on tokens. Offer a handoff early in long sessions.

## Field tests (know-how)

- **Test database:** `\\mekler\Users\gregs\temp\termpointdata\BIOL-TT.db`, master copy `BIOL-TT-master.db` beside it.
  **Reset** (TermPoint closed on both computers; no `BIOL-TT.lock`): copy the master over `BIOL-TT.db`, delete
  `BIOL-TT.lock` / `.lock.tmp` / `.db.tmp` / `-wal` / `-shm`, compare hashes (master `925AD29A…`).
- **Claude's shell sees the share only outside the sandbox** (`dangerouslyDisableSandbox: true`, PowerShell); Git Bash
  can't reach `//mekler/...`. Never open D with SQLite in place: copy it to the scratchpad and query the copy.
- **Helper scripts on GregDeskTop** (`tmp/` is gitignored): `tmp\lockwatch.ps1` (samples the lock file every 20 s),
  `tmp\zzzt_check.py`, `tmp\handtest_lock.py takeover|stale|clear <db>`.
- **Logs:** GregDeskTop `%APPDATA%\TermPoint\Logs\`; GregSurface copies to `\\mekler\Users\gregs\temp\surface-logs\`.
- **"Sleeps":** freeze TermPoint instead (Resource Monitor → CPU → Suspend / Resume Process); trust the lock samples,
  not the moment.
- **Clumsy** (filter `tcp.DstPort == 445 or tcp.SrcPort == 445`): degraded = Lag 110 ms + Drop ~2 %. Open TermPoint
  before starting clumsy (A6).
- **MSIX:** a lower version can't install over a higher one; uninstall first (deletes logs and kept changes).
  `build-msix.ps1` writes `C:\temp\termpoint-msix`; delete its `publish` folder after a build.

## This machine (GregDeskTop, the dev computer)

- **Dev console:** `C:\Users\gregs\source\repos\dev-console\dev-console.bat` (sideload/Store MSIX, licenses, WASM,
  macOS); its `config.ps1` holds the Store identity values.
- **Never kill processes by image name**; only by PID, and only your own.
- `python` here is Inkscape's; use **`py -3.11`**. Controlled Folder Access blocks test runners writing into
  `Documents`. Git Bash `sed -i` turns CRLF files into LF.
- `src/TermPoint/Properties/launchSettings.json` is local. `tools/ScrollAnchorProbe`: re-run after every Avalonia
  upgrade. `grep.exe.stackdump` in the repo root is untracked junk; never commit it.
- **Store-app "won't open until restart":** a Windows bug (stale package-family cache); `IApplicationActivationManager`
  gives the real HRESULT, Process Monitor filtered on the package family name shows the lookups.
