using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SchedulingAssistant.ViewModels;
using SchedulingAssistant.Views;
using System.Linq;

namespace SchedulingAssistant.Services;

/// <summary>
/// Defines all tour step PreAction/MidActions/PostAction callbacks in one place.
/// Each entry's key must match the <c>x:Key</c> of a <c>TourStepData</c>
/// in <c>TourContent.axaml</c>. The dictionary is passed to
/// <see cref="TourCatalog.Initialize"/> at startup and on Ctrl+Shift+T hot-reload.
/// </summary>
internal static class TourActionDefinitions
{
    /// <summary>
    /// Tracks popups whose <see cref="Popup.IsLightDismissEnabled"/> was suppressed
    /// during a tour step, so they can be restored on cleanup.
    /// </summary>
    private static readonly Dictionary<Popup, bool> _suppressedPopups = new();

    /// <summary>
    /// Builds the complete actions dictionary for all tour steps that need
    /// PreAction/MidActions/PostAction callbacks.
    /// </summary>
    public static Dictionary<string, TourStepActions> Build() => new()
    {
        // ── Layout Orientation ──────────────────────────────────────────────

        // PreAction: none (section panel is always visible)
        // MidAction 1: collapse all sections so user sees the compact view
        // PostAction: expand them back
        ["layout.section-panel"] = new(
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    GetViewModel()?.SectionListVm.CollapseAll();
                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                GetViewModel()?.SectionListVm.ExpandAll();
                return Task.CompletedTask;
            }),

        // ── Filter Orientation ──────────────────────────────────────────────

        // PreAction: open the Tags popup, suppress light-dismiss
        // MidAction 1: select the first real tag (grid filters)
        // MidAction 2: close the popup so user sees the filtered grid
        // PostAction: deselect tag, ensure popup closed, restore light-dismiss
        ["filter.tags-open"] = new(
            PreAction: () =>
            {
                var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
                if (toggle is not null)
                    toggle.IsChecked = true;
                SuppressLightDismiss("ScheduleGridPanel", "TagsToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var firstTag = GetViewModel()?.ScheduleGridVm.Filter.Tags
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstTag is not null)
                        firstTag.IsSelected = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                try
                {
                    var firstTag = GetViewModel()?.ScheduleGridVm.Filter.Tags
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstTag is not null)
                        firstTag.IsSelected = false;

                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),

        // PreAction: open the Instructor popup, suppress light-dismiss
        // MidAction 1: select the first instructor
        // MidAction 2: close the popup so user sees the filtered grid
        // PostAction: deselect instructor, ensure popup closed, restore light-dismiss
        ["filter.instructor-open"] = new(
            PreAction: () =>
            {
                var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                if (toggle is not null)
                    toggle.IsChecked = true;
                SuppressLightDismiss("ScheduleGridPanel", "InstructorToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstInstructor is not null)
                        firstInstructor.IsSelected = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                try
                {
                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstInstructor is not null)
                        firstInstructor.IsSelected = false;

                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),

        // PreAction: open the Instructor Overlay popup, suppress light-dismiss
        // MidAction 1: select an instructor to activate the overlay
        // MidAction 2: close the popup so user sees the overlay on the grid
        // PostAction: clear overlay, ensure popup closed, restore light-dismiss
        ["filter.overlay-open"] = new(
            PreAction: () =>
            {
                var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorOverlayToggle");
                if (toggle is not null)
                    toggle.IsChecked = true;
                SuppressLightDismiss("ScheduleGridPanel", "InstructorOverlayToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.NamedInstructors.FirstOrDefault();
                    if (firstInstructor is not null)
                        GetViewModel()?.ScheduleGridVm.Filter.SetInstructorOverlay(firstInstructor.Id);
                    return Task.CompletedTask;
                },
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorOverlayToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                try
                {
                    GetViewModel()?.ScheduleGridVm.Filter.ClearOverlay();

                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorOverlayToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),

        // PreAction: open the Instructor popup, suppress light-dismiss
        // MidAction 1: select the sentinel (unstaffed) entry
        // MidAction 2: close the popup so user sees the filtered grid
        // PostAction: ensure popup closed, restore light-dismiss
        ["filter.unstaffed-open"] = new(
            PreAction: () =>
            {
                var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                if (toggle is not null)
                    toggle.IsChecked = true;
                SuppressLightDismiss("ScheduleGridPanel", "InstructorToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => t.IsSentinel);
                    if (firstInstructor is not null)
                        firstInstructor.IsSelected = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                try
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),

        // ── Filter by Tag ──────────────────────────────────────────────────

        // PreAction: suppress light-dismiss on Tags popup so Next clicks aren't swallowed
        // MidAction 1: open the Tags popup and select the first real tag
        // PostAction: deselect the tag, close the popup, restore light-dismiss
        ["filter.tags"] = new(
            PreAction: () =>
            {
                SuppressLightDismiss("ScheduleGridPanel", "TagsToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
                    if (toggle is not null)
                        toggle.IsChecked = true;

                    var firstTag = GetViewModel()?.ScheduleGridVm.Filter.Tags
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstTag is not null)
                        firstTag.IsSelected = true;

                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                try
                {
                    var firstTag = GetViewModel()?.ScheduleGridVm.Filter.Tags
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstTag is not null)
                        firstTag.IsSelected = false;

                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "TagsToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),

        // ── Filter by Instructor ───────────────────────────────────────────

        // PreAction: suppress light-dismiss on Instructor popup
        // MidAction 1: open the Instructor popup and select the first real instructor
        // PostAction: deselect instructor, close popup, restore light-dismiss
        ["filter.instructor"] = new(
            PreAction: () =>
            {
                SuppressLightDismiss("ScheduleGridPanel", "InstructorToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = true;

                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstInstructor is not null)
                        firstInstructor.IsSelected = true;

                    return Task.CompletedTask;
                }
            },
            PostAction: () =>
            {
                try
                {
                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => !t.IsSentinel);
                    if (firstInstructor is not null)
                        firstInstructor.IsSelected = false;

                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),
    };

    // ── Light-dismiss suppression ───────────────────────────────────────────

    /// <summary>
    /// Finds the <see cref="Popup"/> associated with a named <see cref="ToggleButton"/>
    /// and sets <see cref="Popup.IsLightDismissEnabled"/> to false. The original value
    /// is stashed so <see cref="RestoreAllLightDismiss"/> can restore it.
    /// </summary>
    /// <param name="parentName">Named parent control in MainView.</param>
    /// <param name="toggleName">Named ToggleButton whose sibling Popup to suppress.</param>
    private static void SuppressLightDismiss(string parentName, string toggleName)
    {
        var popup = FindSiblingPopup(parentName, toggleName);
        if (popup is null) return;

        if (!_suppressedPopups.ContainsKey(popup))
            _suppressedPopups[popup] = popup.IsLightDismissEnabled;

        popup.IsLightDismissEnabled = false;
    }

    /// <summary>
    /// Restores <see cref="Popup.IsLightDismissEnabled"/> on all suppressed popups
    /// and clears the tracking dictionary. Idempotent — safe to call multiple times.
    /// Called by the overlay VM on every step transition, dismiss, and hide as a safety net.
    /// </summary>
    public static void RestoreAllLightDismiss()
    {
        foreach (var (popup, originalValue) in _suppressedPopups)
        {
            try
            {
                popup.IsLightDismissEnabled = originalValue;
            }
            catch
            {
                // Control may have been disposed — safe to ignore
            }
        }
        _suppressedPopups.Clear();
    }

    /// <summary>
    /// Finds the <see cref="Popup"/> sibling of a named <see cref="ToggleButton"/>.
    /// In the AXAML pattern, each ToggleButton has a Popup immediately following it
    /// at the same level in the visual tree (both children of the same StackPanel/Grid).
    /// </summary>
    private static Popup? FindSiblingPopup(string parentName, string toggleName)
    {
        var toggle = FindDescendant<ToggleButton>(parentName, toggleName);
        if (toggle?.Parent is not Panel panel) return null;

        var toggleIndex = panel.Children.IndexOf(toggle);
        if (toggleIndex < 0) return null;

        for (int i = toggleIndex + 1; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is Popup popup)
                return popup;
        }

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the current <see cref="MainWindowViewModel"/> from DI.
    /// </summary>
    private static MainWindowViewModel? GetViewModel()
    {
        return App.Services.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
    }

    /// <summary>
    /// Resolves the <see cref="MainView"/> from the current application's main window.
    /// Returns null when running in WASM or before the window is loaded.
    /// </summary>
    private static MainView? GetMainView()
    {
        return (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.FindControl<MainView>("MainViewControl");
    }

    /// <summary>
    /// Finds a named descendant of a named parent control in MainView.
    /// Walks the visual tree, so the child can be inside a UserControl or DataTemplate.
    /// </summary>
    /// <typeparam name="T">Expected control type (e.g. ToggleButton, ComboBox).</typeparam>
    /// <param name="parentName">The <c>x:Name</c> of a control reachable via <c>FindControl</c> from MainView.</param>
    /// <param name="childName">The <c>x:Name</c> of the descendant to find.</param>
    /// <returns>The control, or null if either parent or child is not found.</returns>
    private static T? FindDescendant<T>(string parentName, string childName) where T : Control
    {
        var mainView = GetMainView();
        if (mainView is null) return null;

        var parent = mainView.FindControl<Control>(parentName);
        if (parent is null) return null;

        return parent.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(c => c.Name == childName);
    }
}
