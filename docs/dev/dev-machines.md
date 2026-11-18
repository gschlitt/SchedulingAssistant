# Dev machines: local quirks

Facts about Greg's computers that change how commands must be run. Read this when a build, test run or script behaves
oddly, or before writing large output to disk.

## All machines

- **Never kill processes by image name** (`taskkill /IM` once killed Greg's own python.exe). Kill only by PID, and only
  processes you started.
- **Git Bash `sed -i` turns CRLF files into LF.** Edit with the Edit tool or a byte-exact script.
- **Controlled Folder Access** blocks test runners from writing into `Documents`.
- `src/TermPoint/Properties/launchSettings.json` is local. `grep.exe.stackdump` in the repo root is untracked junk:
  never commit it.
- **HTML wireframes (`designs/wireframes/`) in Claude's browser pane:** the pane shows a local file as a `data:`
  snapshot that sometimes doesn't run the page's script (blank mock), can't be reloaded (`location.reload()` fails),
  and is replaced after every edit. Before driving it from JavaScript, wait about a second and check the mock has
  rendered; never run the page's script a second time by hand, because doubled click handlers make every toggle fire
  twice. Greg reviews them in a real browser.

## GregDeskTop (the main dev computer)

- **C: is nearly full.** Put build and test output on drive E (for example `-o E:/sa-verify`, NetworkProbe to
  `E:\netprobe-build`). Delete it with `cd /e && rm -rf ./name`: a safety check blocks the absolute `rm -rf E:/...` form.
  E: is a USB hard disk, so disk-heavy work there is slow.
- **`python` is Inkscape's.** Use `py -3.11`.
- **Dev console:** `C:\Users\gregs\source\repos\dev-console\dev-console.bat` (sideload and Store MSIX, licenses, WASM,
  macOS). Its `config.ps1` holds the Store identity values.
- **Helper scripts** (in the gitignored `tmp/`):
  - `tmp\lockwatch.ps1`: samples the lock file every 20 s.
  - `tmp\zzzt_check.py`.
  - `tmp\handtest_lock.py takeover|stale|clear <db>`.
  - `tmp\run48.ps1`: starts `E:\sa-48\TermPoint.exe`, waits, prints the open timeline, closes.
  - `tmp\open_timeline.ps1`: timeline of the latest starts, from today's log.
- **Slow-link open runs:** check the added lag first. A missing-file probe on the share should take about one added round
  trip. Clickable `E:\` links in the Code tab don't start that exe.
