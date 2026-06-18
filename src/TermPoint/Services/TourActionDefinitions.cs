using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using TermPoint.ViewModels;
using TermPoint.ViewModels.Management;
using TermPoint.Views;
using System.Linq;

namespace TermPoint.Services;

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

        // ── Find Unstaffed Sections ────────────────────────────────────────

        // PreAction: suppress light-dismiss on Instructor popup
        // MidAction 1: open the Instructor popup and select the sentinel (unstaffed) entry
        // PostAction: close popup only — leave sentinel selected for next step's context
        ["filter.unstaffed"] = new(
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

                    var sentinel = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => t.IsSentinel);
                    if (sentinel is not null)
                        sentinel.IsSelected = true;

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

        // ── Instructor Overlay ─────────────────────────────────────────────

        // PreAction: suppress light-dismiss on Instructor Overlay popup
        // MidAction 1: open overlay popup, select first instructor
        // PostAction: clear overlay, close popup, also clear unstaffed filter from previous step
        ["overlay.instructor"] = new(
            PreAction: () =>
            {
                SuppressLightDismiss("ScheduleGridPanel", "InstructorOverlayToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("ScheduleGridPanel", "InstructorOverlayToggle");
                    if (toggle is not null)
                        toggle.IsChecked = true;

                    var firstInstructor = GetViewModel()?.ScheduleGridVm.Filter.NamedInstructors.FirstOrDefault();
                    if (firstInstructor is not null)
                        GetViewModel()?.ScheduleGridVm.Filter.SetInstructorOverlay(firstInstructor.Id);

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

                    // Clean up the unstaffed filter left active by the previous step
                    var sentinel = GetViewModel()?.ScheduleGridVm.Filter.Instructors
                        .FirstOrDefault(t => t.IsSentinel);
                    if (sentinel is not null)
                        sentinel.IsSelected = false;
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
                return Task.CompletedTask;
            }),

        // ── Instructor Flyout ──────────────────────────────────────────────

        // PostAction: open the instructor flyout so the next step can target it
        ["open-instructor-1"] = new(
            PostAction: () =>
            {
                GetViewModel()?.NavigateToInstructorsCommand.Execute(null);
                return Task.CompletedTask;
            }),

        // MidAction 1: select the first instructor to reveal workload panels
        ["open-instructor-2"] = new(
            MidActions: new Func<Task>[]
            {
                () =>
                {
                    var vm = GetViewModel()?.FlyoutPage as InstructorListViewModel;
                    var first = vm?.Instructors.FirstOrDefault();
                    if (first is not null)
                        vm!.SelectedInstructor = first;
                    return Task.CompletedTask;
                }
            }),

        // PreAction: open the workload mailer panel
        // PostAction: close the instructor flyout
        ["open-instructor-5"] = new(
            PreAction: () =>
            {
                var vm = GetViewModel()?.FlyoutPage as InstructorListViewModel;
                if (vm is not null)
                    vm.ShowWorkloadMailer = true;
                return Task.CompletedTask;
            },
            PostAction: () =>
            {
                GetViewModel()?.CloseFlyoutCommand.Execute(null);
                return Task.CompletedTask;
            }),

        // ── Section Edit ───────────────────────────────────────────────────

        // MidAction 1: collapse all sections so user sees the compact view
        // PostAction: expand them back
        ["section.collapse"] = new(
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

        // ── Adding a Section ───────────────────────────────────────────────

        // MidAction 1: select the 4th section, fire Add (editor opens below it)
        // No PostAction — editor stays open for the next card
        ["section.add"] = new(
            MidActions: new Func<Task>[]
            {
                async () =>
                {
                    var vm = GetViewModel()?.SectionListVm;
                    if (vm is null) return;

                    // Select the 4th section item so Add inserts below it
                    var fourth = vm.SectionItems
                        .OfType<SectionListItemViewModel>()
                        .Skip(3).FirstOrDefault();
                    if (fourth is not null)
                        vm.SelectedItem = fourth;

                    await vm.AddCommand.ExecuteAsync(null);
                }
            }),

        // ── Section Editor: Full Add-and-Edit Walkthrough ───────────────────
        //
        // MidAction 1: pick the 2nd subject and 1st course number
        // MidAction 2: pick the 1st code pattern (auto-fills section code)
        // MidAction 3: fire ApplyPattern1 to create meeting rows
        // MidAction 4: set start time, block length, meeting type on first meeting
        // MidAction 5: open instructor chooser, select 3rd instructor
        // MidAction 6: close instructor chooser, open tag chooser, select 1st tag
        // MidAction 7: close tag chooser
        // PostAction: save the section and clean up
        ["section.editor-course"] = new(
            PreAction: () =>
            {
                SuppressLightDismiss("SectionViewPanel", "InstructorToggle");
                SuppressLightDismiss("SectionViewPanel", "TagsToggle");
                return Task.CompletedTask;
            },
            MidActions: new Func<Task>[]
            {
                // MidAction 1: pick subject + course number
                () =>
                {
                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    if (editVm is null) return Task.CompletedTask;

                    // Pick the 2nd subject
                    var subject = editVm.Subjects.Count > 1
                        ? editVm.Subjects[1] : editVm.Subjects.FirstOrDefault();
                    if (subject is not null)
                        editVm.SelectedSubject = subject;

                    // Pick the 1st available course number
                    var courseNum = editVm.CourseNumbers?.FirstOrDefault();
                    if (courseNum is not null)
                        editVm.SelectedCourseNumber = courseNum;

                    return Task.CompletedTask;
                },

                // MidAction 2: pick the 1st code pattern (auto-fills section code)
                () =>
                {
                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    if (editVm?.CodePatterns.Count > 0)
                        editVm.SelectedPattern = editVm.CodePatterns[0];

                    return Task.CompletedTask;
                },

                // MidAction 3: fire meeting pattern to create meeting rows
                () =>
                {
                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    if (editVm?.HasPattern1 == true)
                        editVm.ApplyPattern1Command.Execute(null);

                    return Task.CompletedTask;
                },

                // MidAction 4: set time, duration, meeting type on the first meeting.
                // Uses CommitStartTime/CommitBlockLength so the text→hours unit
                // conversion is handled correctly (SelectedBlockLength is in hours,
                // but AvailableBlockLengthStrings may show minutes).
                async () =>
                {
                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    var meeting = editVm?.Meetings.FirstOrDefault();
                    if (meeting is null) return;

                    // Pick the 2nd available start time via text + commit
                    if (meeting.AvailableStartTimeStrings.Count > 1)
                    {
                        meeting.StartTimeText = meeting.AvailableStartTimeStrings[1];
                        await meeting.CommitStartTimeCommand.ExecuteAsync(null);
                    }

                    // Pick the 1st available block length via text + commit
                    if (meeting.AvailableBlockLengthStrings.Count > 0)
                    {
                        meeting.BlockLengthText = meeting.AvailableBlockLengthStrings[0];
                        await meeting.CommitBlockLengthCommand.ExecuteAsync(null);
                    }

                    // Pick the 1st meeting type
                    if (meeting.MeetingTypes.Count > 0)
                        meeting.SelectedMeetingTypeId = meeting.MeetingTypes[0].Id;
                },

                // MidAction 5: open instructor chooser, select 3rd instructor
                () =>
                {
                    var toggle = FindDescendant<ToggleButton>("SectionViewPanel", "InstructorToggle");
                    if (toggle is not null)
                        toggle.IsChecked = true;

                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    if (editVm?.InstructorSelections.Count > 2)
                        editVm.InstructorSelections[2].IsSelected = true;

                    return Task.CompletedTask;
                },

                // MidAction 6: close instructor chooser, open tag chooser, select 1st tag
                () =>
                {
                    var instrToggle = FindDescendant<ToggleButton>("SectionViewPanel", "InstructorToggle");
                    if (instrToggle is not null)
                        instrToggle.IsChecked = false;

                    var tagToggle = FindDescendant<ToggleButton>("SectionViewPanel", "TagsToggle");
                    if (tagToggle is not null)
                        tagToggle.IsChecked = true;

                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    if (editVm?.TagSelections.Count > 0)
                        editVm.TagSelections[0].IsSelected = true;

                    return Task.CompletedTask;
                },

                // MidAction 7: close tag chooser
                () =>
                {
                    var tagToggle = FindDescendant<ToggleButton>("SectionViewPanel", "TagsToggle");
                    if (tagToggle is not null)
                        tagToggle.IsChecked = false;

                    return Task.CompletedTask;
                }
            },
            PostAction: async () =>
            {
                try
                {
                    var editVm = GetViewModel()?.SectionListVm.EditVm;
                    if (editVm is not null)
                        await editVm.SaveCommand.ExecuteAsync(null);
                }
                finally
                {
                    RestoreAllLightDismiss();
                }
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
    /// Gets the current <see cref="MainWindowViewModel"/> from DI, or falls back
    /// to <see cref="MainWindow.DataContext"/> when <see cref="App.Services"/> is
    /// not yet initialized (e.g. during the pre-wizard desktop tour).
    /// </summary>
    private static MainWindowViewModel? GetViewModel()
    {
        if (App.Services?.GetService(typeof(MainWindowViewModel)) is MainWindowViewModel vm)
            return vm;
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.DataContext as MainWindowViewModel;
    }

    /// <summary>
    /// Resolves the <see cref="MainView"/> from the current application lifetime.
    /// Works on both desktop (IClassicDesktopStyleApplicationLifetime) and
    /// WASM/browser (ISingleViewApplicationLifetime).
    /// </summary>
    private static MainView? GetMainView()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.FindControl<MainView>("MainViewControl");

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            return singleView.MainView as MainView;

        return null;
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
