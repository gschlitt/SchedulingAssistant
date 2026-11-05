# TileWidthProbe

A regression check for the Schedule grid's **tile widths** (1.2.4 item 2.13, task 1). The grid works out how wide each
tile must be from the measured text plus the tile's chrome (`Views/GridView/TileChrome.cs`, values in `AppLayout.axaml`).
This probe renders the REAL `ScheduleGridView` over the REAL `ScheduleGridViewModel`, built over a COPY of a real
database, in a window parked off-screen, then walks the visual tree and reports every ellipsis-trimmed label that was
given less width than its text needs. **The report must show no clipped labels.** Run it after changing any tile
resource (`TilePaddingHorizontal`, `Tile*Thickness`, `TileContentPadding`, `TileEntryRowPadding`, `TileFlag*`,
`TileMeasuringMargin`), the tile font, the Avalonia version, or the width code in `ScheduleGridView`.

The app's own `App.axaml` resources are loaded at run time, so colours, layout numbers and fonts are the shipping
ones. The probe uses the app's code unchanged: no seams, no overrides.

Slices (each rendered at every "Text:" size in `ScheduleGridViewModel.FontSizeOptions`; default 11):
- `s1_400_CIVL_MECH`: 400-level CIVL + MECH, one semester
- `s1b_400_selected_flagged`: the same, with a few lines flagged and a few selected (in memory only), the narrowest-slot lines
- `s2_300_CIVL_ELEC_MECH`: 300-level CIVL + ELEC + MECH
- `s4_multisem_400_CIVL_MECH`: slice 1 with Term 1 and Term 2 shown (semester-colour borders)

Not part of `TermPoint.sln`, not built by CI. Needs a desktop session. It expects the UBC data (Winter Term 1 / 2 and the
subjects above); on another database the slice names will not match.

## Run
```
copy UBC1-TT.db (and any -wal/-shm) somewhere first; NEVER point the probe at a live database
dotnet run --project tools/TileWidthProbe -c Release -- <db-copy> <output-folder> <repo-root> [margin-px]
```
Writes `<slice>_f<size>.png` for every render and `REPORT.md` (canvas widths, per-day widths, every clipped label) into the
output folder. Settings are redirected into `<output-folder>/settings`, so the real `settings.json` is never touched.
The optional `margin-px` overrides `TileMeasuringMargin` for the run, to find the smallest margin with no clipping.

`TILEPROBE_FONTCHECK=1` instead prints the width and height of sample text measured off the tree (default font, then the
theme font the grid borrows) next to the same text in the tree, then exits. Use it to confirm the measuring font still
matches the drawn font.

Disk: the build is a few hundred MB; delete `tools/TileWidthProbe/bin|obj` and `src/TermPoint/bin/Release|obj/Release`
afterwards, then run `dotnet restore src/TermPoint/TermPoint.csproj` (building the probe leaves the app's restore state broken).
