using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the inline Add/Edit form in the Section Prefixes flyout.
/// Communicates results back to the parent list via callbacks.
/// </summary>
public partial class SectionPrefixEditViewModel : ViewModelBase
{
    private readonly SectionPrefix _target;
    private readonly Action<SectionPrefix> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _prefixExists;

    /// <summary>Whether this form is for a new record (true) or editing an existing one (false).</summary>
    public bool IsNew { get; }

    /// <summary>Form title displayed above the fields ("Add" or "Edit").</summary>
    public string Title => IsNew ? "Add" : "Edit";

    /// <summary>
    /// The ordered list of campus choices, including a leading "(none)" entry.
    /// </summary>
    public List<CampusOption> CampusOptions { get; }

    /// <summary>The prefix text being edited (e.g. "AB", "A#").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _prefix = string.Empty;

    /// <summary>The currently selected campus option (may be the "(none)" sentinel).</summary>
    [ObservableProperty] private CampusOption? _selectedCampus;

    /// <summary>
    /// Whether the section designator that follows this prefix is a number or a letter.
    /// Drives the two radio buttons in the edit form.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNumberDesignator))]
    [NotifyPropertyChangedFor(nameof(IsLetterDesignator))]
    private DesignatorType _designatorType = DesignatorType.Number;

    /// <summary>True when the designator type is Number; bound to the "Number" radio button.</summary>
    public bool IsNumberDesignator
    {
        get => DesignatorType == DesignatorType.Number;
        set { if (value) DesignatorType = DesignatorType.Number; }
    }

    /// <summary>True when the designator type is Letter; bound to the "Letter" radio button.</summary>
    public bool IsLetterDesignator
    {
        get => DesignatorType == DesignatorType.Letter;
        set { if (value) DesignatorType = DesignatorType.Letter; }
    }

    /// <summary>
    /// Validation error message to display beneath the Prefix field,
    /// or null when the input is valid.
    /// </summary>
    public string? ValidationError
    {
        get
        {
            var trimmed = Prefix.Trim();
            if (trimmed.Length == 0) return null;
            if (char.IsDigit(trimmed[^1])) return "Prefix must not end with a number.";
            if (_prefixExists(trimmed)) return $"\"{trimmed}\" already exists.";
            return null;
        }
    }

    private bool CanSave() => Prefix.Trim().Length > 0 && ValidationError is null;

    /// <summary>
    /// Initializes the edit form.
    /// </summary>
    /// <param name="target">The model object to populate on save.</param>
    /// <param name="isNew">True when adding a new prefix; false when editing.</param>
    /// <param name="campusOptions">Campus choices including the leading "(none)" entry.</param>
    /// <param name="onSave">Called with the updated model when the user saves.</param>
    /// <param name="onCancel">Called when the user cancels.</param>
    /// <param name="prefixExists">
    /// Predicate returning true if the given prefix text is already taken
    /// (excluding the current record when editing).
    /// </param>
    public SectionPrefixEditViewModel(
        SectionPrefix target,
        bool isNew,
        List<CampusOption> campusOptions,
        Action<SectionPrefix> onSave,
        Action onCancel,
        Func<string, bool> prefixExists)
    {
        _target = target;
        IsNew = isNew;
        CampusOptions = campusOptions;
        _onSave = onSave;
        _onCancel = onCancel;
        _prefixExists = prefixExists;

        Prefix = target.Prefix;
        DesignatorType = target.DesignatorType;
        SelectedCampus = campusOptions.FirstOrDefault(c => c.Id == target.CampusId)
                         ?? campusOptions[0]; // fall back to "(none)"
    }

    /// <summary>Writes field values back to the model and invokes the save callback.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _target.Prefix = Prefix.Trim();
        _target.DesignatorType = DesignatorType;
        _target.CampusId = SelectedCampus?.Id; // null when "(none)" is selected
        _onSave(_target);
    }

    /// <summary>Discards changes and invokes the cancel callback.</summary>
    [RelayCommand]
    private void Cancel() => _onCancel();
}

/// <summary>
/// A campus choice item used in the campus ComboBox within the prefix edit form.
/// </summary>
/// <param name="Id">
/// The campus SchedulingEnvironmentValue ID, or null for the "(none)" sentinel entry.
/// </param>
/// <param name="DisplayName">The campus name shown in the dropdown.</param>
public record CampusOption(string? Id, string DisplayName);
