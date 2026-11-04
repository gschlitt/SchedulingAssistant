# FieldTest: scripted field tests for TermPoint (1.2.4 items 8.8 and 8.9)

One PowerShell console on the Hyper-V host drives every computer in a field test. It resets the shared test
database, starts, kills, freezes and resumes TermPoint by PID, freezes whole VMs, samples the lock file, lists the
test sections in the database and in each computer's kept changes, and checks each computer's log against a
scenario file. The product is not changed.

The computers:
- **this computer** (the console; also a TermPoint user);
- **Hyper-V VMs** made by `New-TestComputer.ps1`, reached by PowerShell Direct (no network setup);
- optionally a **real computer** over WinRM (off in the config until it is set up).

## Files

| File | What it is |
|---|---|
| `FieldTest.psm1` | The module: every command below. |
| `New-TestComputer.ps1` | Creates a test VM, or a copy of a finished one. Run elevated. |
| `fieldtest.config.psd1` | **Your** machines, share and paths. Gitignored. Format: `fieldtest.config.example.psd1`. |
| `scenarios\*.psd1` | Log checks per field-test scenario (M1, M2, M4, M5 so far). |
| `python\*.py` | Helpers the module calls: fake lock files, a colleague's edit, section listing. They work only on local copies. |
| `Show-SaveStall.ps1` | The M1 log reader (was `tmp\handtest_savelog.ps1`). |

## One-time setup

1. **Python 3.11** on the console (`py -3.11`) for the section listing and the colleague edit.
2. **Make the first VM.** Download the Windows 11 ISO (microsoft.com/software-download/windows11, "Download Windows
   11 Disk Image (ISO) for x64 devices") to drive E. Then, in an elevated window:
   ```
   powershell -ExecutionPolicy Bypass -File .\New-TestComputer.ps1 -Name TPVM1 -IsoPath E:\ISO\<file>.iso
   ```
   The script prints the setup steps: install Windows, an account **with a password**, the share signed in once in
   File Explorer with "Remember my credentials", the same time zone as the host.

   **Network.** By default the VM uses Hyper-V's built-in **Default Switch**, which shares this computer's connection
   and changes nothing on it. The VM may not find the file server by name: use its IP address instead
   (`Resolve-DnsName <server>` on this computer). Running clumsy on this computer would also slow the VM, so run it on
   the file server. To make the VM a real computer on the LAN, move it to an external switch:
   - Create the switch, either with `-SwitchName 'TermPoint test LAN'` on a new VM, or in Hyper-V Manager's Virtual
     Switch Manager. This computer's network drops for a few seconds while Windows moves its settings.
   - Then run `Connect-VMNetworkAdapter -VMName TPVM1 -SwitchName 'TermPoint test LAN'`.
   - To undo: `Remove-VMSwitch -Name 'TermPoint test LAN' -Force`.
3. **Install TermPoint in the VM** from the console (next section): `Install-TermPoint -Machine TPVM1`, then
   `Start-TermPoint -Machine TPVM1`. Choose the test database in its window, then close it.
4. **Clean-state checkpoint:** `Set-VMDvdDrive -VMName TPVM1 -Path $null`, then
   `Checkpoint-TestComputer -Machine TPVM1 -Name Ready`. Checkpoints hold memory, so restoring one brings back a
   signed-in, running computer.
5. **Third computer** (only when a scenario needs it): shut TPVM1 down, then
   `.\New-TestComputer.ps1 -Name TPVM2 -CopyFrom TPVM1`. Two VMs share this computer's 8 GB: close Visual Studio.

The Windows 11 install checks the VM's virtual TPM and Secure Boot, not the host's CPU, so it works on a host that
can't run Windows 11 itself.

## Each session

In an **elevated** PowerShell window (Hyper-V and PowerShell Direct need it):
```
cd <repo>\tools\FieldTest
Import-Module .\FieldTest.psm1 -Force
Connect-TestShare                           # an elevated window doesn't share your normal sign-in to the server
Connect-TestComputer -Name TPVM1 -SaveCredential   # once; later consoles reuse the saved sign-in
Get-TestComputer                            # every machine, its state and its TermPoint PIDs
```
`-SaveCredential` stores the VM's sign-in with Windows DPAPI (`%LOCALAPPDATA%\TermPointFieldTest\`), readable only by
you on this computer. Windows PowerShell 5.1 and PowerShell 7 both work. Every command has help: `Get-Help Reset-TestDatabase -Full`.

## Commands

**Database** (only ever the `Database` in the config; there is no path parameter to get wrong)
- `Reset-TestDatabase [-Force]`: copies the master over D, deletes the lock and leftovers, checks the hashes, and
  records the time (the default start of the log checks). Refuses while TermPoint runs on any computer, including a
  paused or saved VM. Refuses while a lock file exists unless `-Force`.
- `Get-TestDatabaseState`: D's hash compared with the master, leftovers, and the lock holder with heartbeat age and
  timing scale.
- `Watch-TestLock [-Minutes 60] [-IntervalSeconds 20]`: one line per sample, also saved to the work folder.
- `Get-TestSections [-Machine ...] [-Pattern ZZZT]`: copies D and every computer's kept changes, each D' with its
  `-wal` and `.dirty` marker. It lists the test sections and runs the integrity check on the copies.
- `Set-TestLock -Kind Takeover|Stale|Clear`: "Alice on ALICE-PC" takes the lock now, or leaves a 10-minute-old lock,
  or the lock file is deleted.
- `Edit-TestDatabaseAsColleague`: a colleague's save (M12). The edit is made on a local copy, then renamed onto D in
  one step.

**TermPoint** (by PID only; every PID is checked to be TermPoint first)
- `Install-TermPoint -Machine <m> [-MsixPath] [-CertPath]`: the sideload MSIX from `C:\temp\termpoint-msix\`.
- `Start-TermPoint -Machine <m>`: launches it on that computer's desktop and returns the PID.
- `Get-TermPointProcess [-Machine]`: PID, start time, responding, suspended.
- `Stop-TermPoint -Machine <m> -Id <pid> [-Hard]`: `-Hard` is a true crash (the lock file stays). Without it, a close
  request, like clicking the X.
- `Suspend-TermPoint` / `Resume-TermPoint -Machine <m> -Id <pid>`: freeze only the process (the 1.2.3 "sleep").

**VMs**
- `Suspend-TestComputer -Machine <vm> -Seconds 240 [-Mode Pause|Save]`: freezes the whole computer, network included,
  then reports the guest clock's offset at the first answer and 30 s later (also saved in `vm-freezes.txt`).
- `Checkpoint-TestComputer -Machine <vm> -Name <name>`, `Restore-TestComputer -Machine <vm> [-Checkpoint Ready]`.
  A restore discards the VM's logs and kept changes since the checkpoint: collect them first.

**Logs**
- `Get-TermPointLog -Machine <m> [-Since]`: copies the day logs to the work folder.
- `Test-TermPointLog -Scenario <name> -Roles @{ Writer='TPVM1'; Reader='GregDeskTop' } [-Since] [-Timeline]`: shows
  each expected line with its time, as PASS or MISS. Then any forbidden line, any ERROR entry, and the app versions
  seen (more than one means stale bits). Finally PASS or FAIL. `-Timeline` merges both computers' matching lines by time.
- `Show-SaveStall -Machine <m> [-Since] [-HoleOpened]`: the M1 reading of which save step stalled.

## Example: M5, stale-lock takeover with a sleeping writer

```
Reset-TestDatabase
Start-TermPoint -Machine TPVM1            # writer; add an unsaved edit there
Start-TermPoint -Machine GregDeskTop      # reader
Suspend-TestComputer -Machine TPVM1 -Seconds 240
#   ... the reader is offered the takeover: accept, edit, save ...
Test-TermPointLog -Scenario M5-sleep-takeover -Roles @{ Writer='TPVM1'; Reader='GregDeskTop' } -Timeline
Get-TestSections                          # which edits are in D, which are kept on TPVM1
```

## Not known yet (first run answers it)

TermPoint notices a sleep when its **wall clock** jumps by more than 90 s between wake checks. A paused VM's clock
stops. Whether Hyper-V's time sync then steps it forward on resume (wake noticed, like a real sleep) or catches up
slowly (no wake noticed, and the clock runs behind, which is item 6.12's case) is unknown. `Suspend-TestComputer`
prints the offset. If Pause doesn't behave like sleep, try `-Mode Save`. Record what you see in the 8.9 note of
`designs/spec_version_1.2.4_summary.md`.

## Rules the scripts keep

- Only the configured test database. SQLite never opens a file on the share: copies are made first, and the one
  write to D (the colleague edit) is a single rename of a checked copy.
- Processes are found by name but stopped, suspended or resumed only by PID, after checking that the PID is TermPoint.
- Commands that need TermPoint closed everywhere refuse when any computer can't be checked (for example, a
  non-elevated console with VMs in the config).
- Never run TermPoint on the file server.

## Not covered yet

- Some outcomes are dialogs only, with no log line, so they're still checked by eye: the "Restore Unsaved Changes?"
  offer, the kept-changes banner, and the read-only banner. Each scenario file lists them as TODO.
- Scenario files for M6–M12 and S1–S11.
- Item 8.10 (an automated two-machine rig) builds on this.
