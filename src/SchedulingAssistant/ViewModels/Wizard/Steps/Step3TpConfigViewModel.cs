using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.ViewModels.Wizard.Steps;

/// <summary>The three routing choices available to the user in Step 3.</summary>
public enum Step3Choice
{
    /// <summary>Proceed through the full manual configuration steps.</summary>
    Manual,
    /// <summary>Import settings from a .tpconfig file and continue the wizard.</summary>
    Import,
    /// <summary>Exit the wizard now; the user will configure settings inside the application later.</summary>
    ExitNow
}

/// <summary>
/// Step 3 — decide whether to import a .tpconfig file, configure manually, or exit the wizard now.
/// Choosing <see cref="Step3Choice.ExitNow"/> marks initial setup as complete and closes the wizard.
/// </summary>
public partial class Step3TpConfigViewModel : WizardStepViewModel
{
    private readonly Window _ownerWindow;

    public override string StepTitle => "Configuration and Customization ";

    /// <summary>The user's routing choice for the remainder of the wizard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(HasTpConfig))]
    [NotifyPropertyChangedFor(nameof(IsManualChoice))]
    [NotifyPropertyChangedFor(nameof(IsImportChoice))]
    [NotifyPropertyChangedFor(nameof(IsExitNowChoice))]
    private Step3Choice _choice = Step3Choice.Manual;

    /// <summary>Full path to the .tpconfig file chosen by the user.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private string _tpConfigPath = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// The successfully imported config data, or null if the user chose a non-import path.
    /// Populated by <see cref="ValidateAndImport"/> on a successful import.
    /// </summary>
    public TpConfigData? ImportedConfig { get; private set; }

    // ── Radio-button proxy properties (two-way bool per choice) ──────────────

    /// <summary>True when the user has selected the manual configuration path.</summary>
    public bool IsManualChoice
    {
        get => Choice == Step3Choice.Manual;
        set { if (value) Choice = Step3Choice.Manual; }
    }

    /// <summary>True when the user has selected the .tpconfig import path.</summary>
    public bool IsImportChoice
    {
        get => Choice == Step3Choice.Import;
        set { if (value) Choice = Step3Choice.Import; }
    }

    /// <summary>True when the user has chosen to exit the wizard and configure the app later.</summary>
    public bool IsExitNowChoice
    {
        get => Choice == Step3Choice.ExitNow;
        set { if (value) Choice = Step3Choice.ExitNow; }
    }

    /// <summary>Convenience alias consumed by the wizard orchestrator and file-picker visibility binding.</summary>
    public bool HasTpConfig => Choice == Step3Choice.Import;

    /// <summary>
    /// Advancing is allowed for any choice.
    /// When <see cref="Step3Choice.Import"/> is selected a valid file path is required.
    /// </summary>
    public override bool CanAdvance => Choice != Step3Choice.Import || !string.IsNullOrWhiteSpace(TpConfigPath);

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
    /// Returns true on success (or when the manual/exit path is chosen without a file).
    /// Sets <see cref="ErrorMessage"/> and returns false when the file cannot be parsed.
    /// Called by the wizard orchestrator before advancing.
    /// </summary>
    public bool ValidateAndImport()
    {
        if (Choice != Step3Choice.Import)
        {
            ImportedConfig = null;
            return true;   // manual or exit-now path — always valid
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
