# Draft comment for AvaloniaUI/Avalonia#17635 (for Greg to review; not approved wording yet)



---

Still happening on 12.1.6 and on today's nightly (12.2.999-cibuild0101388-alpha), so the recent VirtualizingStackPanel fixes don't cover it.

It isn't specific to Expander: any on-screen item that gets taller makes the list jump, even by 3 px, while the scroll offset stays the same. Small repro (one file, prints numbers): https://github.com/gschlitt/SchedulingAssistant/tree/master/tools/VspGrowDriftRepro

300 items, 50-139 px tall, scrolled down, then one item in mid-viewport grows:

| scrolled to | grown by | item's top before → after |
|---:|---:|---|
| 6000 | 3 px | 252 → 665 |
| 12000 | 500 px | 276 → off screen (the view lands ~40 items earlier) |

With a plain `StackPanel` the item doesn't move.
