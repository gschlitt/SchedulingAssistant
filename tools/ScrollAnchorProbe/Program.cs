using Avalonia;
using Avalonia.Themes.Fluent;

// Off-screen probe for the Section List's scroll anchoring (see README.md). Boots Avalonia (Win32 + Skia
// on Windows) without a visible window, builds a ListBox of 300 variable-height cards through the same
// ListVirtualizationBehavior the app uses, and drives real layout passes. Exits 0 when every check passes.
AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
Application.Current!.Styles.Add(new FluentTheme());

Scenarios.Sanity();
Scenarios.PressAndGrow();
Scenarios.SelectByCodeAndGrow();
Scenarios.UserScroll();
Scenarios.UnrealizedScrollIntoView();
Scenarios.OscillationGuard();
Scenarios.RevealHelper();
Scenarios.Keys();
Scenarios.PanelSwitch();
Scenarios.HiddenList();
Scenarios.Sweep();

// Avalonia keeps background threads alive, so exit explicitly with the result.
Environment.Exit(Report.Finish());
