# Field testing: know-how

How to run TermPoint against the real network share, read its logs, and fake bad networks and sleeping computers.
Read this before any hand test or field test of the shared-database paths (lock, save, takeover, recovery).

## The test database

- **Location:** `\\mekler\Users\gregs\temp\termpointdata\BIOL-TT.db`, with the master copy `BIOL-TT-master.db` beside it.
- **Reset** (TermPoint closed on both computers, and no `BIOL-TT.lock`): copy the master over `BIOL-TT.db`, delete
  `BIOL-TT.lock` / `.lock.tmp` / `.db.tmp` / `-wal` / `-shm`, then compare hashes (master `925AD29A…`).
- **Never open the shared database with SQLite in place.** Copy it to the scratchpad and query the copy.
- **Claude's shell reaches the share only outside the sandbox** (`dangerouslyDisableSandbox: true`, PowerShell).
  Git Bash can't reach `//mekler/...`.

## Logs

- **The real folder:** `%APPDATA%\TermPoint\Logs\`. The sideload and Store builds write there too, so it is the right
  place to send customers.
- **Trap:** Claude's shell runs inside the Claude desktop app's MSIX package, so it sees `%APPDATA%` merged with Claude's
  private copy (`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\TermPoint\`). Anything it launches
  (test runs, TermPoint) writes there as well.
- **To read a real log from Claude's shell**, copy it out with Windows PowerShell (pwsh 7 can't load Appx):
  `Invoke-CommandInDesktopPackage -PackageFamilyName AcademicSolutions.TermPoint_d3gb38k4yrz2r -AppId
  (Get-AppxPackage AcademicSolutions.TermPoint | Get-AppxPackageManifest).Package.Applications.Application.Id -Command
  cmd.exe -Args '/c copy /y %APPDATA%\TermPoint\Logs\app-<date>.log C:\temp\x.txt'`. Or ask Greg.
  GregSurface copies its logs to `\\mekler\Users\gregs\temp\surface-logs\`.

## Faking trouble

- **"Sleeps":** freeze TermPoint instead (Resource Monitor → CPU → Suspend / Resume Process). Trust the lock-file
  samples, not your sense of the moment.
- **Clumsy** (filter `tcp.DstPort == 445 or tcp.SrcPort == 445`; SMB also rides IPv6, so filter on the port alone):
  "degraded" = Lag 110 ms + Drop ~2 %. Open TermPoint before starting clumsy.
- **NetworkProbe** (`tools/NetworkProbe`; its README lists every mode, including `--late-landing`): tests outage
  behaviour client-side.

## Installing builds

- **MSIX:** a lower version can't install over a higher one. Uninstalling first deletes the logs and any kept changes.
- `build-msix.ps1` writes to `C:\temp\termpoint-msix`. Delete its `publish` folder after a build.
- **Store app "won't open until restart":** a Windows bug (a stale package-family cache).
  `IApplicationActivationManager` gives the real HRESULT, and Process Monitor filtered on the package family name shows
  the lookups.
