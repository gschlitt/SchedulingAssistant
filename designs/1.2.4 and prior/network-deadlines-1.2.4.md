# 1.2.4 item 4.10: network file calls outside the save path get a deadline

Status: **locked and built 2026-11-10**. Additions while building: Files → Print PNG uses the same writer (no status line, so its message goes to the notification banner); a refused rotation delete (file open in Excel) is skipped, only a time-out stops rotation. Scope chosen by Greg: all four groups. MinimumVersionToWrite:

By-hand check 2026-11-11 (VS Debug, local database, backups and exports on Mekler, clumsy Drop 100% on port 445) passed: backups, rotation with a CSV open in Excel, all five exports, the empty backup list and the backup result on a dead share, recovery after. The export time-out itself was not reached by hand: with the share dead, Windows' own Save dialog hangs first (it still answers Cancel). Covered by NetworkDeadlinesTests.
**No** (nothing stored in the database changes).

Source: `tools/NetworkProbe/GAP-AUDIT.md` (written 2026-10-22 on 1.2.3), re-checked against `Version_1.2.4` HEAD
`665ffe6`. A1 (the delete of a partial backup) was fixed in 1.2.3. Nothing added since 1.2.3 is unguarded: the section
import's read, template save and report save already go through `NetworkFileOps.RunAsync`; the new `WriteLockService`
lines run inside `TryAcquire`, which every caller wraps. 24 lines remain; 18 are in scope, 6 (A9, the CSV → JSON
migration utility) are reachable only from Dev Tools and are left alone.

## The story

**Today.**

- **Bob keeps his backups on the department share and works over VPN; the share is slow but alive.** Each time he opens
  Save & Backup, TermPoint lists the backup folder once more for every backup (15–20 listings, on the UI thread), and
  each row checks for its CSV again while the list is drawn. At ~200 ms a listing the window stalls 3–4 s every time.
- **Alice opens TermPoint; the share drops seconds after the backup copy finished.** The sections CSV write and the
  rotation (a listing plus deletes) run on the UI thread with no deadline. The window freezes for the SMB timeout
  (30–60 s, longer for a run of calls) and nothing says why.
- **Alice exports the workload report (or course history, the schedule PNG, a shared schedule) to a share that has
  stopped answering**, or clicks Share → create .tpconfig while the database's share is dead. Same freeze, then perhaps
  an error with exception text.

**With the fix.**

- Bob's list costs **one** listing, under a deadline. CSVs are matched to backups in memory, and each row knows whether
  it has a CSV without touching the share again.
- Alice's backup still counts as done (the database copy is what matters, as today). The CSV write gives up after 5 s;
  rotation stops at its first time-out instead of paying 5 s per file, and the next backup finishes the job. One local
  log line each; no new message.
- An export or .tpconfig that times out says, where the status line already is:
  *"TermPoint couldn't save workload.csv: the folder didn't respond. Nothing else was affected. Try again, or choose a
  different folder."*

## The change

### Shared pieces (reuse, not repetition)

- **`NetworkFileOps.WriteAllBytesAsync(path, bytes)`**: deadline-bounded `File.WriteAllBytes`, same shape as the existing
  `WriteAllTextAsync` (log line on time-out, fault seam via `RunAsync`). Callers encode first, so the CSVs keep their
  UTF-8 BOM for Excel. (The existing `WriteAllTextAsync` writes UTF-8 *without* a BOM, so it is not a drop-in.)
- **`ExportFileWriter.WriteAsync(IStorageFile file, byte[] bytes)`** (new, `Services/`): one place for "write a file the
  user picked". Uses `file.TryGetLocalPath()` and `WriteAllBytesAsync` when there is a path; otherwise (a non-filesystem
  provider) falls back to `OpenWriteAsync` under `NetworkFileOps.RunAsync`. Returns a small result: written / timed out /
  failed (with a plain reason). It also owns the user-facing wording, so all exports say the same thing:
  - timed out → the message above;
  - failed (access denied, in use, disk full) → *"TermPoint couldn't save <name>. <plain reason>. Nothing else was
    affected. Try again, or choose a different folder."* No exception text in the UI; the exception goes to the log.

### Group 1: backups (`BackupService`, `BackupEntry`, `SaveAndBackupViewModel`)

1. **Sections CSV (A2).** `WriteSectionsCsvAsync` already builds the whole file in a `StringBuilder`; it encodes it and
   writes with `WriteAllBytesAsync`. A time-out logs one line ("Backup: the sections CSV was not written; the backup
   folder did not respond") and is treated like today's CSV failure (non-fatal, `CsvPath = null`).
2. **Rotation (A3).** `RotateBackups` becomes async: one guarded listing of `prefix_*` (both the `.db` and the
   `_sections.csv` files), selection and CSV matching in memory (`SelectBackupsToDelete` is unchanged), then each delete
   through `NetworkFileOps.DeleteAsync`. **Stops at the first delete that times out** (the folder is not answering; the
   next backup's rotation picks up the rest). A listing time-out skips rotation for this backup. The pure selection
   stays a static, testable function; the in-memory CSV match is a second one.
3. **Backup list (A4).** `GetBackupsAsync` does one guarded listing of `prefix_*` (both kinds) instead of `*.db` plus one
   listing per backup, and matches CSVs in memory with the same helper as rotation. `File.GetCreationTime` (only for a
   file whose name doesn't parse) moves inside the guarded call. `BackupEntry.HasCsv` becomes a plain value set at
   listing time instead of a `File.Exists` on every read, so drawing the list never touches the share.

### Group 2: the `.tpconfig` file (A5)

`TpConfigService.Write` becomes `WriteAsync` and writes through `WriteAllTextAsync`'s byte twin (the file is JSON; no
BOM, same bytes as today). `ShareViewModel` awaits it and shows the timed-out / failed wording through its existing
`SetStatus`. `CreateTpConfigFile` becomes an `async Task` method; CommunityToolkit then generates an async command from the same `[RelayCommand]`.

### Group 3: exports to a picked file (A6, A8)

| Export | Today | After |
|---|---|---|
| Shared schedule (`SharingViewModel` + `SharedScheduleCsvExporter`) | `OpenWriteAsync`, then a `StreamWriter` over the share stream | the exporter writes to a `MemoryStream` (its `Export(Stream…)` signature is unchanged), then `ExportFileWriter` |
| Course history CSV (`CourseHistoryExportViewModel`) | `File.WriteAllText(path, …, BOM)` | encode with BOM, `ExportFileWriter` |
| Workload report CSV (`WorkloadReportViewModel`) | same | same |
| Schedule PNG (`ScheduleGridView.ExportToPng` from `ExportViewModel`) | `bitmap.Save(path)` | the view renders to a `MemoryStream` and returns the bytes (`ExportToPngBytes`); the VM writes them with `ExportFileWriter`. The render stays on the UI thread (it must); only the file write moves. |

The "last export folder" setting is saved only when the write succeeded (today it is saved after any non-throwing
write, which is the same thing).

### Group 4: folder suggestions on mapped network drives (A7)

`AssessCandidateAsync` with `isNetwork: true` runs its whole body (the `Assess`, both `Directory.Exists`, the ancestor
walk and the writability probe) inside one `RunWithTimeout(…, DriveProbeTimeoutMs)`, as `AssessAsync` already does. A
time-out means "no suggestion for that drive", the same as a drive that failed its readiness check. Local candidates
are unchanged.

## The four rules

1. **Atomic rename of a checked copy:** untouched. Nothing here writes the shared database.
2. **Write only with the lock and an unchanged database:** untouched, same reason.
3. **Never delete the only copy of unsaved work:** rotation deletes only backups the retention rule already chose, as
   today; stopping early deletes *less*. No working copy, marker or kept change is touched.
4. **Never confuse the user:** backups get no new message (the CSV and rotation were already silent best-effort steps;
   the database backup still reports as today). Exports and the .tpconfig get one message that says what happened, that
   nothing else was affected, and what to do. No paths, no exception text.

**Cascade check:** the backup still runs from the same three callers (lock acquisition, the periodic timer, the panel)
and still releases `_backupGuard` the same way. A rotation that stops early leaves extra old backups, which the next
rotation removes.

## Risks

- **A write that times out keeps going on its abandoned thread** (that is how `NetworkFileOps.RunAsync` works). A late
  CSV, export or .tpconfig may appear after the message said it wasn't saved. Harmless, and the message never claims
  the file is absent. A late rotation delete removes a file that was chosen for deletion anyway.
- **A share that dies mid-write can leave a partial file:** the same as today.
- **Behaviour on a healthy share is unchanged**, except that the backup list and rotation make fewer calls.
- **Not in the save, lock, restore or recovery paths**, so the "defer risky save-path changes late in a release" rule
  does not apply. `PerformBackupAsync` changes only after the database copy has finished.
- **Encoding:** CSVs keep the UTF-8 BOM; the .tpconfig stays BOM-less JSON. Each gets a test that pins the first bytes.

## Tests

- Rotation: listing time-out → nothing deleted; first delete time-out → stops, later files untouched; CSV matched in
  memory from one listing (existing `BackupRetentionTests` keep passing; the selection function is unchanged).
- Backup list: one listing; `HasCsv` correct without a file check; an unparseable name gets its creation time.
- Sections CSV time-out → backup result success with `CsvPath = null`, one log line.
- `ExportFileWriter`: written / timed out / failed results and wording (time-out via `NetworkFileOps`' time provider or
  fault seam, as the existing `NetworkFileOps` tests do).
- First bytes: CSV exports start with the BOM; .tpconfig doesn't.
- Folder suggestions: a network candidate whose assessment hangs returns no suggestion within the timeout.
- `tools/NetworkProbe`: add cases for the backup CSV, rotation and an export, so the probe can see them on a real dead
  share.
- By hand (optional): Save & Backup list with backups on Mekler over a throttled link (the probe rig), to see the
  listing count drop in the log.
