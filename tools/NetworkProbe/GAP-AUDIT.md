# Gap audit: file-system and SQLite access that can reach a network share without a `NetworkFileOps` deadline

Static audit for `designs/network-probe-spec.md` section 7. Nothing was fixed; this is a list with verdicts.
Audited 2026-10-22 against branch `Version_1.2.3` (HEAD `fc329d5`), by reading the code, not by running it.

The probe can only see calls that go through `NetworkFileOps`. Anything that touches a share path with a plain
`File.*`, `Directory.*`, `FileInfo`, `FileStream`, `StreamWriter`, `SqliteConnection` or a storage-provider stream
bypasses the 5 s race, and on a black-holed share can wait for the SMB redirector (30 to 60 s, longer for a
sequence of calls). If the caller is a UI continuation, the window freezes for that long.

## How it was done

1. Read the path-touching parts of the files the spec names: `CheckoutService.cs`, `WriteLockService.cs`,
   `SessionLifecycle.cs`, `BackupService.cs`, `DatabaseRestore.cs`, `NewFilePlacement.cs`, `DatabaseValidator.cs`,
   `FolderAssessor.cs`, `MainWindow.axaml.cs` (all present).
2. Searched every `src/TermPoint/**/*.cs` for the access forms, then read each hit in context and traced the path
   it receives back to its callers:
   `\b(File|Directory)\.[A-Z]\w*\(`, `new (FileInfo|DirectoryInfo|FileStream|StreamReader|StreamWriter|DriveInfo)`,
   `SqliteConnection\(`, `\.Exists\b`, `OpenReadAsync|OpenWriteAsync`, `TryGet\w*FromPath`, `*PickerAsync`,
   and the helpers that hide these (`TryDelete`, `TryWriteAllText`, `ComputeHash`, `TryReadMarker`, `HasSqliteSignature`).
3. Mapped every caller of the lock-service entry points (`TryAcquire`, `ForceAcquire`, `EnterReaderMode`), because
   `WriteLockService` makes plain calls and is guarded only by its callers.

A "hit" below is one source line that makes the access (callers are listed beside it). A line that is reached both
guarded and unguarded is counted once, under its worst verdict. `NetworkFileOps.cs` itself is the guarded layer and
is what the probe measures; its own internals are covered in section D.

## Verdict counts

| Verdict | Hits (source lines) |
|---|---|
| **share, UNGUARDED** | **25** |
| share, guarded elsewhere | 21 |
| local only | 74 |

Total **120** access lines examined. Of the 25 UNGUARDED: 9 are on the backup folder, the `.tpconfig` file or the
shared-schedule export (sections A1 to A6), 7 are in the folder-suggestion code for mapped network drives (A7), 3 are
user-picked export files (A8) and 6 are in the legacy one-time migration utility (A9). **No unguarded access was found
on the save, lock, restore, crash-recovery or open paths** (`CheckoutService`, `WriteLockService`, `SessionLifecycle`,
`DatabaseRestore`, `NewFilePlacement`, `DatabaseValidator`).

## Assumption every "local only" verdict rests on

`%AppData%\TermPoint` is local. TermPoint keeps D', D'', the dirty marker, `settings.json`, the logs, the
single-instance guard files and the trial file under `Environment.SpecialFolder.ApplicationData`, which is the
**Roaming** profile. On a domain with folder redirection (common at universities) that folder is itself a network
share, and then every "local only" row below becomes "share, UNGUARDED": the working copy, the autosave marker
and every log line would be written through plain calls. Worth confirming on the customer's machines. It is not
counted in the table above.

---

## A. share, UNGUARDED (could wait for the SMB timeout)

Ordered by how close they are to the database and by how likely they are to run on the UI thread.

### A1. Backup folder: delete of a partial backup after a stalled copy

| file:line | call | path | note |
|---|---|---|---|
| `BackupService.cs:287` calling `:1107` | `TryDelete(dbDest)`: `File.Exists` then `File.Delete` | `dbDest` in the backup folder (a share, when the user points backups at one) | Runs **right after** `NetworkFileOps.CopyAsync` (`:283`) reported a stall, i.e. exactly when the share is dead. The `await` on `:283` has no `ConfigureAwait(false)`, and `StartSessionAsync` awaits `PerformBackupAsync` "from the UI thread at write-lock acquisition" (comment at `:267` to `:269`), as does the Save and Backup panel's own backup command (`SaveAndBackupViewModel.cs:168`). **The strongest finding in this audit.** |

### A2. Backup folder: the sections CSV

| file:line | call | path | note |
|---|---|---|---|
| `BackupService.cs:898` (from `:311`) | `await File.WriteAllTextAsync(destPath, ...)` | `csvDest` in the backup folder | The create/open inside `WriteAllTextAsync` is synchronous, so being "async" does not bound it: the same shape as the A1 suspect, one level up. `PerformBackupAsync` awaits it, so the backup (and `StartSessionAsync`, and whatever awaits that) waits for the network. |

### A3. Backup folder: rotation

| file:line | call | path | note |
|---|---|---|---|
| `BackupService.cs:913` (from `:322`) | `Directory.GetFiles(folder, ...)` | backup folder | Plain synchronous listing in `RotateBackups`, called inline from `PerformBackupAsync` inside a `try/catch` that handles exceptions, not waits. |
| `BackupService.cs:1107` (from `:922`, `:929`) | `File.Exists` + `File.Delete` via `TryDelete(oldest)` and `TryDelete(companionCsv)` | old backups in the backup folder | Same helper as A1. |
| `BackupService.cs:1053` (from `:928`) | `Directory.GetFiles(folder, stem + "_*_sections.csv")` in `FindCompanionCsv` | backup folder | One more directory listing per backup that is rotated out. |

### A4. Backup list (restore screen)

| file:line | call | path | note |
|---|---|---|---|
| `BackupService.cs:475` calling `:1053` | `FindCompanionCsv(folder, f)` for every backup file | backup folder | After the one guarded listing (`:464` to `:465`), the `Select` makes **one extra `Directory.GetFiles` per backup** on the continuation thread. `GetBackups()` (`:444`) is a `GetAwaiter().GetResult()` wrapper, so a sync caller blocks on it too. The only current caller is async: `SaveAndBackupViewModel.cs:234`. |
| `BackupService.cs:476` | `File.GetCreationTime(f)` | backup file | Only evaluated when the file name does not parse as a backup name (`??`), so rare. |
| `Models/BackupEntry.cs:23` | `File.Exists(CsvPath)` in the `HasCsv` property | companion CSV in the backup folder | A stat of the share **on every read of the property**; read by `SaveAndBackupViewModel.cs:297` for the list text, i.e. on the UI thread during binding. |

### A5. `.tpconfig` file written next to the database

| file:line | call | path | note |
|---|---|---|---|
| `Services/TpConfigService.cs:37` (from `ViewModels/Management/ShareViewModel.cs:98`) | `File.WriteAllText(path, json)` | `<D's folder>\<abbrev>-TT.tpconfig`: the database's folder, a share | Called straight from a UI command. The **read** side of the same service is guarded (`Step3TpConfigViewModel.cs:127`); the write is not. |

### A6. Shared-schedule export (a share by design)

| file:line | call | path | note |
|---|---|---|---|
| `ViewModels/Management/SharingViewModel.cs:215` (writes in `Services/SharedScheduleCsvExporter.cs:44`) | `file.OpenWriteAsync()`, then a synchronous `StreamWriter` over the stream, then the `await using` dispose (which flushes) | the user-picked file; the code comments say shared-schedule folders "live on a network folder by design" | The **import** side of this view model was hardened with a `NetworkFileOps.RunAsync` read (`:116` to `:117`); the **export** side was not. Open, every write and the closing flush are plain calls on the UI thread. |

### A7. Folder suggestions for mapped network drives (wizard and New Database)

| file:line | call | path | note |
|---|---|---|---|
| `FolderAssessor.cs:257` calling `:73` (`Directory.Exists`), `:78` to `:421` (`FindExistingAncestor` loop), `:79` to `WriteAccessProbe.cs:34`, `:38`, `:45` (`FileStream CreateNew`, `File.Delete`, `File.Exists`) | `Assess(candidatePath)` is the **synchronous** assessment | `<drive>:\TermPoint[\abbrev]` on a mapped network drive (`SuggestLocationsAsync` phase 2, `:237`, `isNetwork: true`) | Only the writability probe at `:274` to `:278` is wrapped in `RunWithTimeout`, and only in the "folder does not exist" branch. The `Assess` call itself, and `Directory.Exists` at `:263`, `FindExistingAncestor` at `:269` and `Directory.Exists` at `:291`, run raw on the iterator's continuation (the UI thread: `NewDatabaseViewModel.cs:317`, `Step2DatabaseViewModel.cs:167`). The drive's `IsReady` was checked within 3 s just before, so the exposure is a share that dies in that window, but there is no deadline if it does. |
| `FolderAssessor.cs:263`, `:291` | `Directory.Exists(candidatePath)` | same | Counted with the row above. |

### A8. User-picked export destinations (a share is possible, not by design)

| file:line | call | path | note |
|---|---|---|---|
| `ViewModels/Management/CourseHistoryExportViewModel.cs:210` | `File.WriteAllText(path, ...)` | the Save dialog's choice | The dialog's start folder is reachability-gated; the write is not. |
| `ViewModels/Management/WorkloadReportViewModel.cs:206` | `File.WriteAllText(path, ...)` | the Save dialog's choice | Same. |
| `Views/GridView/ScheduleGridView.axaml.cs:1257` (from `ExportViewModel.cs:73`, `MainWindowViewModel.cs:1136`) | `bitmap.Save(outputPath, ...)` | the Save dialog's choice | Same. |

### A9. Legacy one-time migration utility (CSV to JSON)

| file:line | call | path | note |
|---|---|---|---|
| `Migration/MigrationRunner.cs:98`, `:104`, `:168` | `Directory.CreateDirectory`, `File.ReadAllLinesAsync`, `File.WriteAllTextAsync` | user-picked folders | "Migration: CSV to JSON (one-time utility)" flyout (`MainWindowViewModel.cs:1036`). |
| `Migration/Phase2Importer.cs:294`, `:300`, `:307` | `Directory.Exists`, `Directory.GetFiles`, `File.ReadAllTextAsync` | user-picked folder | Same utility. |

(A8 and A9 are not on the save, lock, backup, restore or recovery paths the spec names; they are listed for
completeness because they take a user-chosen path that can be on a share.)

---

## B. share, guarded elsewhere

These make plain calls but every caller wraps them. The note says where.

| file:line | call | guarded by |
|---|---|---|
| `WriteLockService.cs:981` | `File.Open(..., CreateNew, ...)` + `WriteLockData` + dispose (flush) in `TryCreateOnce` | `NetworkFileOps.RunAsync(() => _lockService.TryAcquire(...))` at `CheckoutService.cs:690`, `:2399` (restore), `:3498` (re-acquire after removal). `TryAcquire` has no other caller. |
| `WriteLockService.cs:997` | `File.Exists(lock)` | same |
| `WriteLockService.cs:950` | `File.Delete(lock)` in `TryReclaimDeadHolder` | same |
| `WriteLockService.cs:1078` | `File.ReadAllText(lock)` in `ReadCurrentHolder` | same, and `ForceAcquire`, `EnterReaderMode` below |
| `WriteLockService.cs:709` | `File.Delete(lock)` in `ForceAcquire` | `CheckoutService.cs:907` (`RunAsync(... ForceAcquire ...)`) |
| `WriteLockService.cs:1158` | `File.ReadAllText(lock)` in `RecheckStaleHolder` | same (it runs only inside `ForceAcquire`) |
| `WriteLockService.cs:814` | `File.Exists(lock)` in `EnterReaderMode` | `CheckoutService.cs:1041`, `:3766` |
| `CheckoutService.cs:1959` | `new FileInfo(path)` and `.Length` (the save's stat of D) | the `RunAsync` it sits in, `:1956` |
| `BackupService.cs:465` | `Directory.GetFiles(folder, ...)` in `GetBackupsAsync` | the `RunAsync` at `:464` |
| `BackupService.cs:571` | `new SqliteConnection` in `CheckIntegrityWithSummary(path)` when the path is on the share | `NetworkFileOps.CheckIntegrity*Async` (`DatabaseValidator.cs:94`). Its other callers pass local files (`BackupService.cs:278`, `CheckoutService.cs:2759`, `DatabaseRestore.cs:794`, `LocalCopyIntegrity.cs:93`). |
| `DatabaseValidator.cs:233` | `new FileStream` in `HasSqliteSignature(path)` | `RunAsync` at `DatabaseValidator.cs:165` to `:166` for the share. Other callers pass local files (`MinimumVersionToWrite.cs:155`, `DatabaseRestore.cs:790`). |
| `DatabaseRestore.cs:857`, `:858` | `File.ReadAllBytes`, `File.WriteAllBytes` of the half-file (DEBUG fault helper) | the `RunAsync` at `:855` |
| `NewFilePlacement.cs:53` | `Directory.CreateDirectory(folder)` | the `RunAsync` at `:52` |
| `Licensing/LicenseValidator.cs:40`, `:46` | `File.Exists`, `File.ReadAllText` of `termpoint.lic` in the database's folder | `App.axaml.cs:190` (`RunAsync(... EvaluateAccess ...)`); `EvaluateAccess` has no other caller |
| `Services/TpConfigService.cs:58`, `:59` | `File.Exists`, `File.ReadAllText` | `Step3TpConfigViewModel.cs:127` (`RunAsync`) |
| `ViewModels/Management/SharingViewModel.cs:117` | `File.ReadAllBytes(localPath)` | the `RunAsync` around it |
| `ViewModels/Management/SharingViewModel.cs:128` | `file.OpenReadAsync()` | taken only for a non-filesystem provider (no UNC path), per the code comment |
| `Services/StorageProviderExtensions.cs:39` | `provider.TryGetFolderFromPathAsync(path)` | `DirectoryExistsAsync` first (`:36`); only a time-of-check gap remains |
| `FolderAssessor.cs:73`, `:79`, `:421`, `WriteAccessProbe.cs:34`, `:38`, `:45` (counted in A7, not again here) | the same `Assess` code as A7, **when reached through** `AssessAsync` | `AssessAsync`'s own 3 s `RunWithTimeout` (`FolderAssessor.cs:104`). Not `NetworkFileOps`, so no log line and no fault seam, but bounded. All outside callers use `AssessAsync` (`NewDatabaseViewModel.cs:240`, `:267`, `Step1aExistingDbViewModel.cs:119`, `:145`, `Step2DatabaseViewModel.cs:190`, `:216`). |
| `ViewModels/Wizard/StartupWizardViewModel.cs:562` calling `WriteAccessProbe.cs:34`, `:38`, `:45` (counted in A7) | `CanCreateFileIn(folder)` | the `RunAsync` at `:561` |

(Everything else that reaches the share in `CheckoutService`, `SessionLifecycle`, `DatabaseRestore`,
`NewFilePlacement`, `DatabaseValidator`, `WriteLockService`'s heartbeat, poll and release, and `BackupService`'s copy
and directory check is a direct call to a `NetworkFileOps` method, not a plain call, so it is not a hit. The `grep` of
`NetworkFileOps.` shows about 100 such call sites.)

---

## C. local only

Paths here are D', D'', their markers and temporaries under `%AppData%\TermPoint\working`, files in the system temp
folder, or the app's own install folder. See the assumption above about folder redirection.

| file:line | call | path |
|---|---|---|
| `CheckoutService.cs:736`, `:939`, `:3066`, `:4144` | `Directory.CreateDirectory(_workingDir)` | working folder |
| `CheckoutService.cs:1079`, `:1707`, `:2677`, `:2678`, `:2707`, `:2857`, `:2900`, `:3264`, `:3731`, `:4073` | `File.Exists` | D', its marker, `.savetmp` (`WorkingPath` is only ever assigned `ComputeWorkingPath` or `ReadOnlyWorkingPath`, `:702`, `:733`, `:1076`, `:4230`). `:3264` tests `File.Exists` *before* `WorkingPath != SourcePath`; harmless while the invariant holds. |
| `CheckoutService.cs:2662`, `:2671` | `File.Exists` | `.savetmp` and fresh-copy temp beside D' |
| `CheckoutService.cs:2579` | `File.Move(tmpPath, ReadOnlyWorkingPath)` | D''.tmp to D'' |
| `CheckoutService.cs:4222` | `File.Move(roTmpPath, ReadOnlyWorkingPath)` | D''.tmp to D'' |
| `CheckoutService.cs:2922`, `:2927`, `:2930` | `File.Exists`, `File.Move` (archive of a damaged local copy) | D'/D'' and `.corrupt-*` |
| `CheckoutService.cs:3266`, `:3733` | `File.Delete(WorkingPath)` | D' |
| `CheckoutService.cs:4048` | `File.ReadAllText(markerPath)` in `TryReadMarker` | the dirty marker beside D' (callers `:1077`, `:2758`) |
| `CheckoutService.cs:4299` | `new FileStream` in `ComputeHash` | D', `.savetmp`, D''.tmp, copy destinations: all local (callers `:991`, `:1723`, `:2558`, `:4187`, `:4210`, `:4399`, `:4425`) |
| `CheckoutService.cs:4470`, `:4471` | `new SqliteConnection` x2 in `BackupSqliteDatabase` | D' to a local snapshot (callers `:1698`, `:2868`) |
| `CheckoutService.cs:4489` | `File.Delete` in `TryDelete` | every caller passes a local path (checked every call site: working copy, `.dirty`, `-wal`, `-shm`, `.savetmp`, `*.tmp` of D'', copy destinations) |
| `CheckoutService.cs:4509` | `File.WriteAllText` in `TryWriteAllText` | only caller is `WriteDirtyMarker` (`:4031`): the marker beside D' |
| `CheckoutService.cs:3086` | `NetworkFileOps.MoveWithRetry(tmpPath, WorkingPath)` | both local |
| `BackupService.cs:274`, `:304` | `TryDelete(localTmp)` | `D'.backuptmp` |
| `BackupService.cs:410`, `:425` | `new SqliteConnection` x2 (`VACUUM INTO`, then journal mode) | D' and the local temp |
| `BackupService.cs:1140` | `new SqliteConnection` (`IsolatedReadContext`) | D' |
| `DatabaseRestore.cs:415` | `Directory.CreateDirectory(_localDirectory)` | system temp |
| `DatabaseRestore.cs:806`, `:817`, `:834` | `SqliteConnection` x2, `FileStream` | the staged local copy |
| `DatabaseRestore.cs:1014` | `File.Delete` in `TryDelete` | staged temp and its sidecars |
| `NetworkFileOps.cs:710` (`ComputeHash`, called by `DatabaseRestore.cs:468` and `NewFilePlacement.cs:127`) | `new FileStream` | the staged local copy and the finished local file |
| `Data/DatabaseContext.cs:104`, `:110`, `:656` | `new SqliteConnection`, `Directory.CreateDirectory` | D'/D'' only ("always the local copy", `App.axaml.cs:368`) |
| `Data/DatabaseContext.cs:498`, `:499` | `File.Exists`, `File.Delete` of the pre-migration backup | beside D' |
| `Data/DatabaseContext.cs:130` (not counted: the access lines are in `WriteAccessProbe.cs`) | `WriteAccessProbe.CanCreateFileIn(dir)` | D''s folder, i.e. local |
| `Services/MinimumVersionToWrite.cs:168`, `:230` | `new SqliteConnection` (read and raise the stamp) | `WorkingPath` (D'): `CheckoutService.cs:1317`, `:1282` |
| `Services/NewDatabaseFile.cs:127`, `:161` | `new SqliteConnection`, `File.Delete` | a temp file in the system temp folder |
| `Services/DatabaseInstanceGuard.cs:194`, `:199` | `Directory.CreateDirectory`, `new FileStream` (guard file) | `%AppData%\TermPoint\working\guards` |
| `Services/AppSettings.cs:290`, `:298`, `:336`, `:338`, `:339` | `File.Exists`, `ReadAllText`, `CreateDirectory`, `WriteAllText`, `Move` | `%AppData%\TermPoint\settings.json` |
| `Services/FileAppLogger.cs:158`, `:191`, `:224`, `:226`, `:228`, `:229` | `CreateDirectory`, `AppendAllLines`, `Exists`, `GetFiles`, `GetLastWriteTime`, `Delete` | `%AppData%\TermPoint\Logs` (written on **every** log call, from whatever thread logs) |
| `Licensing/TrialService.cs:74`, `:79`, `:109`, `:110` | `File.Exists`, `ReadAllText`, `CreateDirectory`, `WriteAllText` | `%AppData%\TermPoint\trial.json` |
| `Services/FolderAssessor.cs:360`, `:376`, `:378`, `:382`, `:394`, `:402` | `Directory.Exists`, `File.Exists`, `File.ReadAllText` | OneDrive / Dropbox / Google Drive / iCloud sync roots on the local disk |
| `ViewModels/Management/HelpViewModel.cs:83` | `File.Exists` | the app's install folder (`Help\`) |

**No direct access at all (0 hits):** `SessionLifecycle.cs` (it reaches the share only through `CheckoutService` and
`NetworkFileOps`: `:973`, `:983`, `:1376`), `DatabaseSwitchPrecheck.cs`, `DatabaseSwitchRecovery.cs`,
`RestoreDestination.cs`, `LocalCopyIntegrity.cs` (calls `NetworkFileOps` at `:93`). `MainWindow.axaml.cs` has no file
access of its own; `:1670` is a Save dialog that returns only a path, which `ExportRecoveredCopyAsync` then places
through `NewFilePlacement` (guarded).

---

## D. Inside `NetworkFileOps` itself (what the probe measures; not a bypass)

These are not "unguarded calls" but are where a guard could still leak, and are what the probe's cases exercise:

| file:line | call | concern |
|---|---|---|
| `NetworkFileOps.cs:383` | `dst!.WriteAsync(...)` | **Finding A1.** The race against `Task.Delay` starts only after `WriteAsync` returns its task. The open (`:325`), the close (`:364`) and the hash open (`:228`) run under `Task.Run`; the write does not. The probe's `copy-cm` case is built to answer this. |
| `NetworkFileOps.cs:409` | `stream.ReadAsync(...)` | Same shape for the read path (the field saw this one caught: `ComputeHash (read)`); the probe's `hash-cm` case is the control. |
| `NetworkFileOps.cs:590`, `:688` | `Task.Run(operation)` raced against `Task.Delay` | The generic path. Safe against a blocking call. The probe's CB cases check it for each named helper. |

---

## What to take away

1. **Worst first, in this order:** A1 (`BackupService.cs:287`), A2 (`:898`), A3 (rotation), A4 (list), then A5 and A6.
   They are all reachable on the UI thread, they all sit on a share the user is *expected* to use (a backup folder, a
   shared-schedule folder, the database folder), and A1 runs at the moment the share is known to be dead.
2. The save, lock, restore, recovery and open paths of `CheckoutService`, `WriteLockService`, `DatabaseRestore` and
   `NewFilePlacement` have **no unguarded access**. Their remaining weakness is internal to `NetworkFileOps` (D).
3. `WriteLockService` is guarded only by its callers, and only as a unit: `TryAcquire` (a release wait of up to 12 s
   plus create, read, delete and re-create) shares a single 5 s deadline, and a thread abandoned by that deadline
   keeps going and can still create the lock file later. That is a design property rather than a gap, but it means
   a future caller that forgets the `RunAsync` wrapper would be unguarded with no compiler help.
4. Confirm the `%AppData%` assumption on the customer's machines (see the top).
