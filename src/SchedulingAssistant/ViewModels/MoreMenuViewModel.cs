using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.ViewModels.Management;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SchedulingAssistant.ViewModels;

/// <summary>
/// Hosts the "More" flyout. Its left rail lists whichever low-priority top-bar items
/// are currently overflowed; selecting an entry resolves the matching management
/// ViewModel from DI and displays it in the right pane.
///
/// Keys correspond to the x:Name values on the source buttons in
/// <see cref="MainWindow"/>'s top menu bar — see <see cref="KnownItems"/>.
/// </summary>
public partial class MoreMenuViewModel : ViewModelBase
{
    /// <summary>
    /// Catalog of every low-priority top-bar item that could appear in More. Order
    /// matches the declared (left-to-right) order of the source buttons.
    /// </summary>
    private static readonly (string Key, string DisplayName, Type ViewModelType)[] KnownItems =
    {
        ("NavSchedulingEnvironment", "Scheduling Environment", typeof(SchedulingEnvironmentViewModel)),
        ("NavAcademicYears",         "Academic Years",         typeof(AcademicYearListViewModel)),
        ("NavConfiguration",         "Configuration",          typeof(ConfigurationViewModel)),
        ("NavExport",                "Export",                 typeof(ExportHubViewModel)),
        ("NavHelp",                  "Help",                   typeof(HelpViewModel)),
    };

    private readonly IServiceProvider _services;

    /// <summary>
    /// Entries currently shown in the left rail. Rebuilt by <see cref="SetHiddenKeys"/>.
    /// </summary>
    public ObservableCollection<MoreMenuEntry> Entries { get; } = new();

    /// <summary>
    /// Currently-selected rail entry, or null when nothing is selected. Setting this
    /// resolves <see cref="ContentPage"/> through DI and raises <see cref="TitleChangeRequested"/>.
    /// </summary>
    [ObservableProperty]
    private MoreMenuEntry? _selectedEntry;

    /// <summary>
    /// The ViewModel hosted in the right pane. <see cref="ViewLocator"/> resolves its view.
    /// </summary>
    [ObservableProperty]
    private object? _contentPage;

    /// <summary>
    /// True when the rail has no entries — used to drive a "No additional items" placeholder.
    /// </summary>
    public bool HasNoEntries => Entries.Count == 0;

    /// <summary>
    /// Fired when the VM wants the owning flyout title to change. Payload is the full
    /// title ("More" or "More › Scheduling Environment"). <see cref="MainWindowViewModel"/>
    /// subscribes and forwards to its FlyoutTitle property.
    /// </summary>
    public event Action<string>? TitleChangeRequested;

    /// <summary>
    /// Wired by <see cref="MainWindowViewModel"/> so Configuration, when hosted in-flyout,
    /// can still trigger a database restore. See <see cref="OnSelectedEntryChanged"/>.
    /// </summary>
    public Func<string, Task>? ConfigurationRestoreCallback { get; set; }

    public MoreMenuViewModel(IServiceProvider services)
    {
        _services = services;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoEntries));
    }

    /// <summary>
    /// Rebuilds <see cref="Entries"/> from the supplied key set. Preserves the current
    /// <see cref="ContentPage"/> even when the previously-selected entry is no longer in
    /// the list (window widened while More was open); the user can still dismiss.
    /// </summary>
    /// <param name="keys">x:Name keys of the currently-hidden top-bar items.</param>
    public void SetHiddenKeys(IEnumerable<string> keys)
    {
        var set = new HashSet<string>(keys);
        Entries.Clear();
        foreach (var (key, display, vmType) in KnownItems)
        {
            if (!set.Contains(key)) continue;
            Entries.Add(new MoreMenuEntry
            {
                Key = key,
                DisplayName = display,
                ViewModelType = vmType
            });
        }

        // If the previously-selected entry is no longer overflowed, clear selection
        // but keep the content pane visible (per spec — don't yank it out from under).
        if (SelectedEntry is not null && !set.Contains(SelectedEntry.Key))
        {
            // Avoid re-triggering ContentPage resolution; setting to null in our
            // partial handler only resets the title, which is what we want.
            var previouslySelected = SelectedEntry;
            SelectedEntry = null;
            // Title reverts to "More" — the user's prior content is still shown, though.
            _ = previouslySelected; // silence unused warning
        }
    }

    /// <summary>
    /// When the user picks a rail entry, resolve its ViewModel through DI and update the title.
    /// Special-cases <see cref="ConfigurationViewModel"/> to wire its restore callback.
    /// </summary>
    partial void OnSelectedEntryChanged(MoreMenuEntry? oldValue, MoreMenuEntry? newValue)
    {
        if (newValue is null)
        {
            TitleChangeRequested?.Invoke("More");
            return;
        }

        try
        {
            var vm = _services.GetRequiredService(newValue.ViewModelType);

#if !BROWSER
            // Configuration has a callback the host normally wires before display; replicate it.
            if (vm is ConfigurationViewModel cfg && ConfigurationRestoreCallback is not null)
                cfg.SaveAndBackupVm.RestoreCallback = ConfigurationRestoreCallback;
#endif

            ContentPage = vm;
        }
        catch (Exception ex)
        {
            ContentPage = new ErrorViewModel($"Failed to open {newValue.DisplayName}: {ex.Message}");
        }

        TitleChangeRequested?.Invoke($"More › {newValue.DisplayName}");
    }
}
