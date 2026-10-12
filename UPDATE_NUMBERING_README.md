# Version numbering conventions

This file collects TermPoint's conventions about version numbers. There are two numbers, and they mean
different things:

| Number | Where it lives | What it means |
|---|---|---|
| **App version** (e.g. `1.2.3`) | `<Version>` in `src/TermPoint/TermPoint.csproj` | Which build this is. |
| **`MinimumVersionToWrite`** (e.g. `1.2.5`) | Inside each database, in the `AppConfiguration` table | The oldest TermPoint allowed to **edit** that database. |

---

## 1. The app version

- **Set it in one place:** `<Version>` in `src/TermPoint/TermPoint.csproj`. Everything else is derived
  from it.
  - `build-msix.ps1` (Microsoft Store and MSIX builds) turns it into the four-part MSIX version
    (`1.2.3` → `1.2.3.0`).
  - `publish.ps1` (Velopack Windows build) asks for the version and writes it into `<Version>`.
  - The running app reads it from its assembly. It is shown in About and in the logs, and the
    `MinimumVersionToWrite` check compares against it.
- **macOS:** the GitHub workflow `publish-macos.yml` takes the *package* version from the git tag
  (`v1.2.3`) or the workflow input, but the app itself is still built with the csproj `<Version>`. Keep the
  tag and `<Version>` identical.
- **"What's new" announcements:** a new entry in `AppAnnouncements.All` must have `SinceVersion` equal
  to the release's `<Version>`.
- **The app version has no database meaning.** You may bump major, minor or patch for any reason, such
  as marketing, size of the release, or a fresh start. Rules about who may edit a database are carried
  only by `MinimumVersionToWrite` (decided 2026-10-11).

---

## 2. `MinimumVersionToWrite`

### What it is

> **MinimumVersionToWrite is not the database's version, and not the version that last wrote it. It is
> the oldest TermPoint allowed to edit this database; older versions open it read-only. It only ever goes
> up. It is raised only when a release answers Yes to: "Could the new version save anything the old
> version wouldn't have?"**

Why it exists: departments often run mixed versions on one shared database, because Store updates
arrive in the background over days and IT can delay them. An older version can damage data a newer
version wrote.

- **Since 1.2.3, an older version keeps fields it doesn't know about.** It writes them back untouched
  (the `PersistedRecord` unknown-field catch-all).
- **That isn't always enough.** An older version can still save something the newer one wouldn't.
  `MinimumVersionToWrite` stops older versions from editing at all once that is possible.

### When to raise it: ask this for EVERY release

> **Could the new version save anything the old version wouldn't have?**

The answer is **Yes** if either of these is true:

1. **The same starting database plus the same edits gives a different saved result.** For example, a
   bug fix in a save path, a new default, or a changed format.
2. **The new version offers an edit the old one can't make.** For example, a new field, a new setting
   or a new kind of record.

| Change | Answer | Why |
|---|---|---|
| Section-type colours (a new saved field) | **Yes** | New kind of edit |
| 1.2.3's fixes that stop an edit wiping Flag / Capacity | **Yes** | Same edit, different record |
| A CSV import fix | **Yes** | Same file, different records |
| Card layout, scrolling, a crash fix in a view, performance work | No | Nothing saved changes |

**Most releases are No. If the answer is No, do nothing.**

**Raising has a cost.** Every colleague on an older version loses the ability to edit that database
until they update. They can still view it. Don't raise it just in case; raise it when the question says
Yes.

### How to raise it

1. **Set the two constants** in `src/TermPoint/Services/MinimumVersionToWrite.cs`. The question and
   examples are repeated in the comments above them.

   ```csharp
   public const string? ThisReleaseRequires = "1.2.5";                                // this release's <Version>
   public const string? ThisReleaseReason   = "Section-type colours are now saved.";  // one plain-English line
   ```

2. **Update the pinning test.** `ThisRelease_Constants_AreNullInThisRelease` in
   `src/TermPoint.Tests/MinimumVersionToWriteTests.cs` fails on purpose once the constants change. Change
   it to expect the new values (and rename it to match).
3. **Build and run the full test suite.**
4. **Test the release build on database copies only.** An MSIX built with the Store identity and
   installed locally counts as a Store install, so it **will** write the stamp into any database it edits.
5. **In Partner Center, mark the Store submission mandatory**, and allow time for certification and
   roll-out before users depend on the release.
6. **Release notes:** say that databases edited with this version can only be viewed, not edited, by
   older TermPoint versions until those copies update.

**Never set the constants back to null, or lower, in a later release.** A later release still saves the
newer data, so it must keep stamping at least that version. The constants only ever go up, exactly like
the stamp. Change them again only when a later release answers Yes again.

### What happens after a raise (automatic)

- **The first edit writes the stamp.** The first time a Store or direct-download copy of that release
  opens a database for editing, it writes `MinimumVersionToWrite` and `MinimumVersionToWrite.Reason` into
  that database:
  - it never lowers an existing value;
  - it is written into the working copy, so it reaches the shared database in the same save as the data
    it protects.
- **Who is affected:**
  - **TermPoint 1.2.3 or later, if older than the stamp:** opens that database read-only. A red banner
    explains why. Store installs also get an **[Open Microsoft Store]** button, which opens Downloads and
    updates ("Get updates").
  - **TermPoint older than 1.2.3:** doesn't know the stamp exists and ignores it.

### Safe in development

- **These builds never write the stamp:**
  - builds run from Visual Studio (install channel *Development*);
  - sideloaded MSIX packages (install channel *Sideload*).

  You can work against real data while a raise is in progress.
- **Testing the banner wording:** in Debug builds, the environment variable
  `TERMPOINT_INSTALL_CHANNEL=Store` (in `src/TermPoint/Properties/launchSettings.json`) makes the app
  behave as a Store install. Release builds ignore it.
- **Testing the gate:** stamp a *copy* of a database with any SQLite tool:
  `INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ('MinimumVersionToWrite', '9.9.9');`

### Checking or undoing a stamp

- **To see a database's stamp:** run `SELECT key, value FROM AppConfiguration;` in any SQLite browser,
  on a copy.
- **To undo a mistaken raise:**
  1. Set the constants back first, or the next edit raises the stamp again.
  2. Then delete the `MinimumVersionToWrite` and `MinimumVersionToWrite.Reason` rows while **nobody has
     the database open**. Do this on the shared file only with a backup in hand.

### Where it is implemented

| Piece | File |
|---|---|
| Read the stamp, compare versions, raise it, banner wording, Store link | `src/TermPoint/Services/MinimumVersionToWrite.cs` |
| The gate (read-only fallback) and the stamping step, at checkout | `src/TermPoint/Services/CheckoutService.cs` (`ApplyVersionGateAsync`) |
| Install channel detection (Store / Sideload / DirectDownload / Development) | `src/TermPoint/Services/PlatformCapabilities.cs`, `InstallChannel.cs` |
| Banner text and Store button | `MainWindowViewModel.LockStatusMessage` / `ShowOpenStoreButton`, `Views/MainView.axaml` |
| Background and decisions | `spec_version_1.2.3.md`, item 3 (step 3b outcome) |
