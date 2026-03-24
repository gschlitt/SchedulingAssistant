using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>
/// Step 3 — decide whether to import a .tpconfig file.
/// "Yes" shows a file picker; "No" routes to the manual configuration path (step 4).
/// </summary>
public partial class Step3TpConfigViewModel : WizardStepViewModel
{
    private readonly Window _ownerWindow;

    public override string StepTitle => "Import Configuration";

    /// <summary>True when the user selects "I have a .tpconfig file".</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private bool _hasTpConfig = false;

    /// <summary>Full path to the .tpconfig file chosen by the user.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _tpConfigPath = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// The successfully imported config data, or null if the user chose the manual path.
    /// Populated by <see cref="ValidateAndImport"/> on a successful import.
    /// </summary>
    public TpConfigData? ImportedConfig { get; private set; }

    /// <summary>
    /// Advancing is always allowed — the user can pick "No" without any file.
    /// When "Yes" is selected, a valid file path is required.
    /// </summary>
    public override bool CanAdvance => !HasTpConfig || !string.IsNullOrWhiteSpace(TpConfigPath);

    public Step3TpConfigViewModel(Window ownerWindow) => _ownerWindow = ownerWindow;

    [RelayCommand]
    private async Task BrowseTpConfig()
    {
        var result = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Select .tpconfig file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TermPoint Config") { Patterns = ["*.tpconfig"] },
                new FilePickerFileType("All Files")        { Patterns = ["*.*"] }
            ]
        });

        if (result.Count > 0)
        {
            TpConfigPath = result[0].TryGetLocalPath() ?? string.Empty;
            ErrorMessage = string.Empty;
        }
    }

    /// <summary>
    /// Attempts to read and parse the chosen .tpconfig file.
    /// Returns true on success; sets <see cref="ErrorMessage"/> and returns false on failure.
    /// Called by the wizard orchestrator before advancing.
    /// </summary>
    public bool ValidateAndImport()
    {
        if (!HasTpConfig)
        {
            ImportedConfig = null;
            return true;   // manual path — always valid
        }

        if (!TpConfigService.TryRead(TpConfigPath, out var config) || config is null)
        {
            ErrorMessage = "Could not read the .tpconfig file. Check the path and try again, or choose the manual setup path.";
            return false;
        }

        ImportedConfig = config;
        ErrorMessage   = string.Empty;
        return true;
    }
}
