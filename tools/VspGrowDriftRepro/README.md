# VspGrowDriftRepro

A stand-alone repro for an Avalonia bug: in a `ListBox` with the default `VirtualizingStackPanel` and items of
different heights, an item that is on screen gets taller and the list jumps. The item moves up or down the viewport,
or off it, though nothing above it changed and the scroll offset did not change. A plain `StackPanel` keeps it in place.

It uses no TermPoint code, so the folder can be copied as-is into its own repository for the bug report.
It is not part of `TermPoint.sln` and not built by CI.

## Run

```
dotnet run -c Release                                  # prints a table; exit code 1 when the bug shows
dotnet run -c Release -- --interactive                 # a window: select an item, press Grow
dotnet run -c Release -- --interactive --classic       # the same with a plain StackPanel
dotnet run -c Release -p:AvaloniaVersion=12.1.6        # any other Avalonia version
```

The automatic run parks its windows off-screen, so it does not disturb the desktop. The build output is about 600 MB;
delete `bin` and `obj` afterwards (or add `-p:CreateHardLinksForCopyLocalIfPossible=true`).

## Why TermPoint cares

The section list's virtualization is optional and relies on `ListScrollAnchorBehavior` to undo this drift
(`tools/ScrollAnchorProbe` checks that workaround). A fix upstream would let the anchoring be retired.
