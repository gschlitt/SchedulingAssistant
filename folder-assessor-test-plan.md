# FolderAssessor & Location Suggestion — Test Plan

## Overview

The FolderAssessor system serves three integration points:

1. **Wizard Step 2 (new-DB path)** — suggests database locations, assesses the chosen database folder and backup folder, shows non-blocking advisory warnings
2. **Wizard Step 1a (existing-DB path)** — assesses the *parent folder* of the selected .db file for CFA/cloud warnings, assesses the backup folder
3. **File → New flyout** — same suggestion + assessment behavior as wizard Step 2, with config transfer

Each uses `FolderAssessor.CreateForCurrentMachine()` at construction to detect CFA roots and cloud sync roots, then calls `Assess()` (per-folder warnings) and `SuggestLocationsAsync()` (drive enumeration + filtering).

---

## Prerequisites

| Label | Description |
|-------|-------------|
| **Machine A** | Your primary Windows dev machine |
| **Machine B** | Second networked Windows machine |
| **Share** | A shared network folder visible to both machines (mapped or UNC) |
| **OneDrive** | If OneDrive is installed on either machine (common on Windows 10/11) |

Before each test sequence, **delete** `%APPDATA%\TermPoint\settings.json` so the wizard runs fresh. (Under MSIX, that's the virtualized `LocalCache\Roaming\TermPoint\settings.json`.)

---

## Part 1: Suggestion Enumeration

These tests verify that `SuggestLocationsAsync` discovers drives correctly and presents the right candidates.

### 1.1 — Local drives only (no network)

**Setup:** Disconnect or unmap any network drives on Machine A.

| # | Step | Expected |
|---|------|----------|
| 1 | Launch app → wizard → proceed to Step 2 (Database Location) | Suggestions appear as clickable buttons |
| 2 | Inspect suggestion list | Should include: (a) `C:\ProgramData\TermPoint\{abbrev}` labeled "Shared application data folder", (b) `C:\TermPoint\{abbrev}` labeled "System drive". If a second fixed drive (D:, E:) exists: `D:\TermPoint\{abbrev}` labeled "Secondary drive" |
| 3 | Verify priority order | Secondary drive (priority 10) before ProgramData (20) before system drive root (30) |
| 4 | Verify `{abbrev}` | The subfolder matches whatever abbreviation was entered in the earlier wizard step. If blank, suggestions show `TermPoint` with no subfolder |

### 1.2 — With network drive

**Setup:** Map the shared folder as a drive letter on Machine A (e.g. `net use Z: \\MachineB\Share`).

| # | Step | Expected |
|---|------|----------|
| 1 | Launch wizard → Step 2 | Network drive suggestion appears: `Z:\TermPoint\{abbrev}` labeled "Network drive" |
| 2 | Verify priority | Network drive (priority 5) appears **first** in the list, above all local suggestions |
| 3 | Verify writability | The network suggestion only appears if the write probe succeeded (a temp file was created and deleted on the share). If the share is read-only, it should be silently filtered out |

### 1.3 — Network drive timeout

**Setup:** Map a drive to a share on Machine B, then **disconnect Machine B's network** (pull cable / disable adapter) while leaving the drive letter mapped.

| # | Step | Expected |
|---|------|----------|
| 1 | Launch wizard → Step 2 | Suggestions should load within ~5 seconds (3-second per-drive timeout). The unreachable network drive is silently dropped — no hang, no error message |
| 2 | Local suggestions | Still present and correct despite the network failure |

### 1.4 — UNC path (no drive letter)

**Setup:** Remove any mapped drive letters. Access the share only via UNC (`\\MachineB\Share`).

| # | Step | Expected |
|---|------|----------|
| 1 | Launch wizard → Step 2 | No UNC suggestion appears (SuggestLocationsAsync enumerates `DriveInfo.GetDrives()`, which only returns drive letters). This is expected — user must Browse to a UNC path |

### 1.5 — File → New flyout

**Setup:** App already running with a loaded database.

| # | Step | Expected |
|---|------|----------|
| 1 | File → New | Flyout shows same suggestion list as wizard Step 2 |
| 2 | Verify abbreviation | Subfolder is derived from `AppSettings.Current.InstitutionAbbrev` (the institution abbreviation from the current database) |

---

## Part 2: Folder Assessment — CFA Detection

These tests verify that CFA-protected folder warnings appear correctly.

### 2.1 — Database folder under Documents

| # | Step | Expected |
|---|------|----------|
| 1 | Wizard Step 2 → Browse → select `Documents\TermPoint` | Yellow warning banner appears: "This folder is inside a Windows-protected location..." mentioning Windows Defender |
| 2 | Check warning detail | Should reference Ransomware protection → Allow an app through Controlled folder access |
| 3 | Try to advance | Next button is **not** blocked — warning is advisory only |

### 2.2 — Database folder under Desktop

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select Desktop or a subfolder of it | Same CFA warning banner appears |

### 2.3 — Database folder outside known folders

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select `D:\Schedules` or `C:\TermPoint` | No CFA warning |

### 2.4 — CFA warning on backup folder

| # | Step | Expected |
|---|------|----------|
| 1 | Set database folder to a clean location (e.g. `D:\TermPoint`) | No warnings on DB folder |
| 2 | Browse backup folder → select Documents subfolder | CFA warning appears in the **backup** warnings section (separate from DB warnings) |

### 2.5 — Existing-DB path (Step 1a)

| # | Step | Expected |
|---|------|----------|
| 1 | Choose "I have an existing database" → Browse → pick a .db file inside Documents | CFA warning banner appears for the database's parent folder |
| 2 | Note: no "NotWritable" warning | For existing DBs, writability warnings are suppressed (the file is already there; user may open read-only) |

---

## Part 3: Folder Assessment — Cloud Sync Detection

### 3.1 — OneDrive (if installed)

| # | Step | Expected |
|---|------|----------|
| 1 | Wizard Step 2 → Browse → select a folder inside the OneDrive sync root | Yellow warning: "This folder is synced by OneDrive. Cloud sync services can corrupt SQLite databases..." |
| 2 | Check message | Warns about partial sync corruption, suggests non-synced folder |
| 3 | Advancing | Not blocked — advisory only |

### 3.2 — OneDrive Known Folder Move (Documents redirected to OneDrive)

**Setup:** If the user's Documents folder is redirected to OneDrive (common with Microsoft 365), `%USERPROFILE%\Documents` actually resolves to `%USERPROFILE%\OneDrive\Documents`.

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select a folder under Documents (which is under OneDrive) | **Both** CFA and cloud sync warnings appear simultaneously |

### 3.3 — Dropbox (if installed)

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select folder inside Dropbox sync root | Cloud sync warning naming "Dropbox" |

### 3.4 — Google Drive / iCloud (if applicable)

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → folder inside `%USERPROFILE%\Google Drive` or `%USERPROFILE%\iCloudDrive` | Cloud sync warning naming the provider |

### 3.5 — No cloud sync

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → folder on a plain local or network drive | No cloud sync warning |

---

## Part 4: Writability Probing

### 4.1 — Writable local folder

| # | Step | Expected |
|---|------|----------|
| 1 | Wizard Step 2 → click a suggestion or Browse to a writable folder | No "not writable" warning; Next is available |

### 4.2 — Non-writable local folder (simulate with ACL)

**Setup:** Create `C:\ReadOnlyTest`, then deny write permissions:
```powershell
New-Item C:\ReadOnlyTest -ItemType Directory
icacls C:\ReadOnlyTest /deny "$env:USERNAME:(W)"
```

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select `C:\ReadOnlyTest` | "The application cannot write to this folder" warning appears |
| 2 | Clean up | `icacls C:\ReadOnlyTest /remove:d "$env:USERNAME"` then remove folder |

### 4.3 — Folder doesn't exist yet

| # | Step | Expected |
|---|------|----------|
| 1 | Browse or click suggestion for a folder that doesn't exist (e.g. `D:\TermPoint\NewDept`) | No "not writable" warning (the assessor walks up to the nearest existing ancestor and probes *that*). No false alarm |

### 4.4 — Network folder writable

**Setup:** Share a folder on Machine B with read/write permissions for Machine A's user.

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select the mapped network folder | No writability warning; suggestion appears if drive is mapped |

### 4.5 — Network folder read-only

**Setup:** Share a folder on Machine B with **read-only** permissions for Machine A's user.

| # | Step | Expected |
|---|------|----------|
| 1 | Browse → select the read-only network folder | "Not writable" warning appears |
| 2 | Suggestion list | The read-only network drive should **not** appear in suggestions (filtered out by the write probe) |

---

## Part 5: Suggestion Clicking & UI Flow

### 5.1 — Click suggestion sets database folder

| # | Step | Expected |
|---|------|----------|
| 1 | Click any suggestion button | The "Database folder" TextBox populates with the clicked path |
| 2 | Filename section appears | Shows the pre-seeded filename (e.g. `CS-TT.db`) below the folder |
| 3 | Full path preview | Shows combined path: `D:\TermPoint\CS\CS-TT.db` |

### 5.2 — Switch between suggestions

| # | Step | Expected |
|---|------|----------|
| 1 | Click suggestion A, then click suggestion B | Folder changes to B; any warnings from A are replaced by B's assessment |

### 5.3 — Browse overrides suggestion

| # | Step | Expected |
|---|------|----------|
| 1 | Click a suggestion, then Browse to a different folder | Folder updates to the browsed path; warnings recalculate |

### 5.4 — Same-folder warning (backup = database folder)

| # | Step | Expected |
|---|------|----------|
| 1 | Set database folder and backup folder to the same path | Orange advisory appears: "Your backup folder is the same as your database folder..." recommending a different drive |
| 2 | Advancing | Not blocked — recommendation only |

### 5.5 — Filename validation

| # | Step | Expected |
|---|------|----------|
| 1 | Clear the filename field | "Filename cannot be blank" error; Next disabled |
| 2 | Type a name without extension (e.g. `mydata`) | Auto-appends `.db` |
| 3 | Type a name with wrong extension (e.g. `mydata.txt`) | "Filename must end in .db" error |
| 4 | Type invalid characters (e.g. `my:data.db`) | "Filename contains invalid characters" error |

---

## Part 6: Network-Specific Scenarios

### 6.1 — Both machines, shared database folder

**Setup:** Machine A and Machine B both map the same shared folder.

| # | Step | Expected |
|---|------|----------|
| 1 | On Machine A: run wizard → pick network suggestion → complete setup | Database created on the share |
| 2 | On Machine B: run wizard → "I have an existing database" → browse to the .db on the share | Existing DB opens. CFA and cloud warnings assessed against Machine B's environment |
| 3 | Verify lock behavior | Machine A holds the write lock; Machine B sees read-only banner or stale-lock prompt |

### 6.2 — Network goes down during suggestion loading

**Setup:** Start the wizard, then pull the network cable between Step 1 and Step 2.

| # | Step | Expected |
|---|------|----------|
| 1 | Proceed to Step 2 | Suggestions load (may miss network drives due to timeout). No crash, no hang. Local suggestions still appear |

### 6.3 — Network share with CFA overlap

**Setup:** Unlikely but testable: map a network drive letter and also have the path show up under a OneDrive sync root (or manually configure).

| # | Step | Expected |
|---|------|----------|
| 1 | Browse to the overlapping path | Both CFA and/or cloud warnings fire based on the resolved path |

---

## Part 7: File → New Integration

### 7.1 — Suggestions in File → New

| # | Step | Expected |
|---|------|----------|
| 1 | Open an existing database, then File → New | Suggestion list appears with same assessment behavior |
| 2 | Click a suggestion | Database folder field populates; any warnings show |

### 7.2 — Warnings in File → New

| # | Step | Expected |
|---|------|----------|
| 1 | Browse DB folder to a CFA-protected location | CFA warning appears in the flyout |
| 2 | Browse backup folder to OneDrive | Cloud sync warning appears in backup section |
| 3 | Set both folders the same | Same-folder recommendation appears |

### 7.3 — Create database on network drive via suggestion

| # | Step | Expected |
|---|------|----------|
| 1 | Click network drive suggestion → fill in name → Create | Database created successfully on the share. No hang, no timeout |

---

## Part 8: Edge Cases

### 8.1 — No drives available (extreme)

This is more of a unit-test scenario, but if somehow `DriveInfo.GetDrives()` returns nothing (e.g. in a sandboxed environment), `SuggestLocationsAsync` should return an empty list without crashing.

### 8.2 — Very long path

| # | Step | Expected |
|---|------|----------|
| 1 | Browse to a deeply nested folder (path > 200 chars) | Assessment runs without error. Path displays correctly in the UI (may truncate visually but full path is stored) |

### 8.3 — Special characters in path

| # | Step | Expected |
|---|------|----------|
| 1 | Browse to a folder with spaces, parentheses, or non-ASCII characters (e.g. `D:\My Schedules (2026)`) | Assessment and suggestion work correctly. No path-parsing errors |

### 8.4 — Suggestion for folder that already exists

| # | Step | Expected |
|---|------|----------|
| 1 | Create `D:\TermPoint\CS` manually before running wizard | Suggestion shows `AlreadyExists = true`. No functional difference — user can still select it |

---

## Verification Checklist

After all tests, confirm:

- [ ] No crash or hang in any scenario
- [ ] Network timeouts resolve within ~5 seconds (3s per-drive probe)
- [ ] All warnings are advisory — never block Next/Create
- [ ] Warnings clear and recalculate when the folder changes
- [ ] Suggestions appear in priority order (network → secondary → ProgramData → system)
- [ ] Abbreviation subfolder appears correctly in suggestions
- [ ] File → New and Wizard Step 2 behave identically for suggestions and warnings
- [ ] Existing-DB path (Step 1a) suppresses writability warnings but shows CFA/cloud warnings
- [ ] Log file (`%APPDATA%\TermPoint\Logs\app-*.log`) shows no errors from FolderAssessor
