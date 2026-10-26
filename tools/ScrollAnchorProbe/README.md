# ScrollAnchorProbe

An off-screen check of the Section List's **scroll anchoring** (spec_version_1.2.3.md, items 21 and 23).

**Re-run it after every Avalonia upgrade.** The anchoring works around a bug in Avalonia's
`VirtualizingStackPanel` (the list "drifts" when a card changes height). It depends on how that panel
re-estimates sizes, how `ScrollViewer` coerces its offset, and which events fire when. Any of those can
change in a new Avalonia release. A failing check here means the anchoring needs attention before the
upgrade ships. (If upstream ever fixes the panel, the `E1` rows with anchoring **off** will show zero
drift, and the anchoring could be retired.)

It is **not** part of `TermPoint.sln` and **not** built by CI. It is a small console app that is run by hand.

## What it checks

The probe builds a `ListBox` of 300 variable-height cards in a window parked off-screen at
(-20000, -20000), wires it through the same `ListVirtualizationBehavior` the app uses, and drives real
layout passes and synthetic input. It compiles the **real** behaviour sources from
`src/TermPoint/Behaviors` (`ListScrollAnchorBehavior`, `ListVirtualizationBehavior`,
`SuppressPopupScrollBehavior`, `ScopeToken`), never copies of them, with a tiny stub for `App.Logger`.

| Scenario | What it shows |
|---|---|
| `SANITY` | The rig virtualizes (about 8 of 300 cards built) and finds the list's ScrollViewer. |
| `E1`, `E1b` | Press (or select by code) a card, then grow it by 500 px (the inline editor opening). Without anchoring the card's top drifts by hundreds to thousands of pixels; with anchoring it stays put. **Informational**: the last E1 row jumps straight to offset 15000 without scrolling there, which trips the oscillation guard by design. The `SWEEP` is the pass criterion. |
| `E2` | A user scroll (wheel) of +300 px and +5000 px sticks and is not "corrected" back. |
| `E3` | `ListBox.ScrollIntoView` of a card the panel has not built: swallowed without `SuppressPopupScrollBehavior.Bypass()`, works with it. |
| `E4` | The oscillation guard: a saboteur that resizes a card on every layout pass makes the guard trip once, log once, and stop correcting; a wheel tick resumes it. |
| `E5` | `ListScrollAnchorBehavior.ScrollItemIntoView`, the one scroll path: unbuilt card, clipped card, card taller than the viewport (top-aligned), card already in place (left alone), classic panel with anchoring off, and two quick requests (the later wins). |
| `E6` | Keyboard. A PageDown key press scrolls a page and the scroll sticks (the same page scroll made through the API, with no key event, is undone, which is why the key is a user-scroll hint). An arrow key after a selection made by code leaves the newly selected card in place. The same after a click rather than a code selection (sub-case `c`) checks that the anchor survives the panel recycling every container (see below). |
| `E7` | `ListVirtualizationBehavior` swaps the panel and the anchoring flag at run time, directly and through a binding, and the suppression is on whichever panel is active. |
| `E8` | `ScrollItemIntoView` on a hidden list does not throw. |
| `E9` | **The pinned card is virtualized away by its own growth** (spec item 27). The cards have a button. A card near the bottom of the viewport is pressed (or selected by code), keyboard focus goes to the button inside it, as clicking "Add meeting" does (that is what stops the panel keeping the card alive: it appears to protect only the list's "tab once active element", which is then the button, not the card), and the card grows by 1500 or 4000 px in one step, or by 500 and then 900 in two (the editor opens, then a meeting row is added), at 6 start offsets. In every trial the panel recycles the card in the pass of the growth (a layout handler that runs before the anchoring's own counts the passes with no container, so the case checks that it tests the bug). Without the restoration in `ListScrollAnchorBehavior` the card never comes back and the editor is left far down the list; with it the card's top is back within 1 px of where it was pinned. Also checked: a user's wheel scroll of +6000 px away from a pressed card with focus inside it sticks (the restoration is off while the user scrolls; this fails if that guard is removed). |
| `SWEEP` | **The pass criterion.** Press a card, then grow it by 500 px, at 17 start offsets reached by wheel-sized steps. The card's top must stay within 1 px of where it was. |

## How to run

From the repository root:

```
dotnet run --project tools/ScrollAnchorProbe -c Release
```

It takes about 20 seconds after the first build, and needs a desktop session (it creates a real, hidden
window; on Windows it uses Win32 + Skia). The process exit code is 0 when every check passes and 1 otherwise.

Disk: the build output is roughly 600 MB. Delete `tools/ScrollAnchorProbe/bin` and `obj` when you are
done (they are git-ignored). On a nearly full disk, add `-p:CreateHardLinksForCopyLocalIfPossible=true
-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true` to the command: the NuGet files are then hard-linked
into `bin` and the output costs almost no extra space.

## Expected output

The end of a passing run:

```
=== SWEEP  press, then grow +500, anchoring ON; success = card top within 1 px of where it was
  press held within 1 px: 17/17;  press+grow held within 1 px: 17/17
  [PASS] SWEEP: the pressed card holds within 1 px at all 17 start offsets
  [PASS] SWEEP: press + 500 px growth holds within 1 px at all 17 start offsets

ALL 39 CHECKS PASSED
```

Reference numbers for 12.1.3, useful when something fails:

- `E1` / `E1b`, anchoring on, start offsets 3000 to 12000: `driftGrow` is `0.0` in every row. With anchoring off it is
  `1569`, `2419`, `2823`, `4596`, `6364` for start offsets 3000, 6000, 9000, 12000, 15000.
- `E3`: without the bypass the offset stays `0.0`; with it the offset becomes `22799.0` and the card's top is `576.0`.
- `E4`: `offset changed in 1 passes; log lines written: 1`.
- `E9`: in all 24 trials the card has no container at exactly one pass (the pass of the growth) and its final top equals the pinned top (`0.0` difference). Without the restoration it has no container for 81 passes (the whole 80-pass settle) and the final top is `n/a`.
- `SWEEP`: `17/17` and `17/17`.

## Anchor identity (E6 sub-case c)

The anchor is the **data item**, not its container. Clicking a card and then pressing Down makes the panel
recycle every element at once (about 390 px of drift in the probe). The pressed card then gets a new
container, which an earlier version mistook for "the anchor is gone", so that drift went uncorrected
(`next card top 257.0 -> 641.0`). Now the container is looked up again from the item on every pass, and a
selection of a different card replaces the pressed anchor; the probe expects `257.0 -> 257.0`.

## Trying a different Avalonia version

`ScrollAnchorProbe.csproj` pins `AvaloniaVersion` (currently the version in `src/TermPoint/TermPoint.csproj`)
and warns at build time when the two differ. To try a candidate version before changing the app:

```
dotnet run --project tools/ScrollAnchorProbe -c Release -p:AvaloniaVersion=12.1.4
```

When the app moves to a new Avalonia version, update `AvaloniaVersion` in the probe's csproj to match and
re-run.

## Changing the behaviours

If you change `ListScrollAnchorBehavior`, `ListVirtualizationBehavior`, `SuppressPopupScrollBehavior` or
`ScopeToken`, run the probe as well as the unit tests (`dotnet test src/TermPoint.Tests`). The unit tests cover
the pure parts (reveal geometry, rate limiter, key classification, scope token); the probe covers everything
that needs a real layout.
