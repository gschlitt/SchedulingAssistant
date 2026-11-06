# HelpShots

Draws the pictures for the Help article **Filters and Overlays** (`src/TermPoint/Help/schedule-grid-filters.html`)
from the app's REAL filter bar: the real `GridFilterView` over a real `GridFilterViewModel`, with the real `App.axaml`
resources and styles. The data is made up in code (Joe Smith, Sally Ford, BIOL210 …), so no database is opened and no
real names reach the public repo.

In the app only one chooser can be open at a time. For Help, each picture can show several open at once: the named
popups' contents are lifted out of their popups and drawn in place under their buttons, inside the same view, so the
view's own styles still apply. Lists are given a plain item panel for the picture (see `ShowAllItems`); they look the
same, all items showing.

Not part of `TermPoint.sln`, not built by CI. Needs a desktop session (it opens a window parked off-screen).

## Run
```
dotnet run --project tools/HelpShots -- <repo-root>
```
Run it in Debug (the default), NOT `-c Release`: a Release run re-restores `TermPoint.csproj` without its Debug-only
packages (Newtonsoft.Json and others), and the next Debug compile of the app then fails until `dotnet restore` is run.
Writes `src/TermPoint/Help/images/filters/*.png` (drawn at 2x; the article shows them at half size). Settings are
redirected to a temp folder, so the real `settings.json` is never touched.

To see exactly what a 100% screen shows (small details can look fine at 2x and be unreadable at 1x), render at scale 1
into a scratch folder: `dotnet run --project tools/HelpShots -- <repo-root> 1 <scratch-folder>`.

Re-run after any change to the filter bar (`GridFilterView.axaml`, its code-behind, the filter colours or font sizes),
then look at the pictures and commit them with the change. To add a picture, add a `Shot(...)` line in `Program.cs`:
the choices to make, and the choosers to show open.
