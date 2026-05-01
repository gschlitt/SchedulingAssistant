using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// ViewModel for the Section Code Patterns configuration panel.
/// Supports full CRUD for <see cref="SectionCodePattern"/> entities.
/// </summary>
public partial class SectionCodePatternListViewModel : ViewModelBase, IDismissableEditor
{
    private readonly ISectionCodePatternRepository _repo;
    private readonly ICampusRepository _campusRepo;
    private readonly ISchedulingEnvironmentRepository _propertyRepo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    /// <summary>Label shown in the Configuration flyout sidebar.</summary>
    public string DisplayName => "Section Codes";

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<SectionCodePattern> _items = new();
    [ObservableProperty] private SectionCodePattern? _selectedItem;
    [ObservableProperty] private SectionCodePatternEditViewModel? _editVm;

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        if (EditVm is null) return false;
        EditVm.CancelCommand.Execute(null);
        return true;
    }

    /// <param name="repo">Section code pattern repository.</param>
    /// <param name="campusRepo">Campus repository, for the campus pre-fill dropdown.</param>
    /// <param name="propertyRepo">Scheduling environment repository, for section type pre-fill.</param>
    /// <param name="dialog">Service for confirmation and error dialogs.</param>
    /// <param name="lockService">Write lock; gates edit operations in read-only mode.</param>
    public SectionCodePatternListViewModel(
        ISectionCodePatternRepository repo,
        ICampusRepository campusRepo,
        ISchedulingEnvironmentRepository propertyRepo,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _repo        = repo;
        _campusRepo  = campusRepo;
        _propertyRepo = propertyRepo;
        _dialog      = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    /// <summary>Reloads the pattern list from the database.</summary>
    public void Load() =>
        Items = new ObservableCollection<SectionCodePattern>(_repo.GetAll());

    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the campus options list (with a leading "(none)" sentinel) from the database.
    /// </summary>
    private List<CampusOption> BuildCampusOptions()
    {
        var options = new List<CampusOption> { new(null, "(none)") };
        options.AddRange(_campusRepo.GetAll().Select(c => new CampusOption(c.Id, c.Name)));
        return options;
    }

    /// <summary>
    /// Builds the section-type options list (with a leading "(none)" sentinel) from the database.
    /// </summary>
    private List<SectionTypeOption> BuildSectionTypeOptions()
    {
        var options = new List<SectionTypeOption> { new(null, "(none)") };
        options.AddRange(
            _propertyRepo.GetAll(SchedulingEnvironmentTypes.SectionType)
                         .Select(v => new SectionTypeOption(v.Id, v.Name)));
        return options;
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    /// <summary>Opens the Add form for a new pattern.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var pattern = new SectionCodePattern
        {
            SortOrder = Items.Count > 0 ? Items.Max(p => p.SortOrder) + 1 : 0
        };
        EditVm = new SectionCodePatternEditViewModel(
            pattern, isNew: true,
            campusOptions:      BuildCampusOptions(),
            sectionTypeOptions: BuildSectionTypeOptions(),
            onSave:   p => { _repo.Insert(p); Load(); SelectedItem = Items.FirstOrDefault(x => x.Id == p.Id); EditVm = null; },
            onCancel: ()  => EditVm = null,
            nameExists: name => _repo.ExistsByName(name));
    }

    /// <summary>Opens the Edit form for the currently selected pattern.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedItem is null) return;
        var copy = new SectionCodePattern
        {
            Id            = SelectedItem.Id,
            Name          = SelectedItem.Name,
            Prefix        = SelectedItem.Prefix,
            Suffix        = SelectedItem.Suffix,
            UseLetters    = SelectedItem.UseLetters,
            FirstNumber   = SelectedItem.FirstNumber,
            PadWidth      = SelectedItem.PadWidth,
            Increment     = SelectedItem.Increment,
            FirstLetter   = SelectedItem.FirstLetter,
            CampusId      = SelectedItem.CampusId,
            SectionTypeId = SelectedItem.SectionTypeId,
            SortOrder     = SelectedItem.SortOrder,
        };
        EditVm = new SectionCodePatternEditViewModel(
            copy, isNew: false,
            campusOptions:      BuildCampusOptions(),
            sectionTypeOptions: BuildSectionTypeOptions(),
            onSave:   p => { _repo.Update(p); Load(); SelectedItem = Items.FirstOrDefault(x => x.Id == p.Id); EditVm = null; },
            onCancel: ()  => EditVm = null,
            nameExists: name => _repo.ExistsByName(name, excludeId: copy.Id));
    }

    /// <summary>Prompts for confirmation and deletes the selected pattern.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedItem is null) return;
        if (!await _dialog.Confirm($"Delete pattern \"{SelectedItem.Name}\"?")) return;
        _repo.Delete(SelectedItem.Id);
        Load();
    }
}

// ── Support records ───────────────────────────────────────────────────────────

/// <summary>An option entry for the section-type pre-fill dropdown, with a sentinel "(none)" option.</summary>
/// <param name="Id">Section-type property value ID, or null for the "(none)" sentinel.</param>
/// <param name="Name">Display name.</param>
public record SectionTypeOption(string? Id, string Name);

/// <summary>
/// Inline Add/Edit form ViewModel for a single <see cref="SectionCodePattern"/>.
/// Communicates results back to the parent list via callbacks.
/// </summary>
public partial class SectionCodePatternEditViewModel : ViewModelBase
{
    private readonly SectionCodePattern _target;
    private readonly Action<SectionCodePattern> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _nameExists;

    /// <summary>True when adding a new pattern; false when editing.</summary>
    public bool IsNew { get; }

    /// <summary>Form title: "Add Pattern" or "Edit Pattern".</summary>
    public string Title => IsNew ? "Add Code Pattern" : "Edit Code Pattern";

    // ── Observable fields ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _prefix = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _suffix = string.Empty;

    /// <summary>
    /// When true, the incrementing part uses letters (A–Z).
    /// When false, it uses integers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNumberFields))]
    [NotifyPropertyChangedFor(nameof(ShowLetterFields))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(IncrementorPreview))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _useLetters;

    /// <summary>
    /// Text representation of the first number in the sequence (e.g. "1", "100", "001").
    /// Leading zeros indicate the desired pad width.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstNumberError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(IncrementorPreview))]
    private string _firstNumberText = "1";

    /// <summary>Text representation of the increment step (e.g. "1", "10", "100").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IncrementError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(IncrementorPreview))]
    private string _incrementText = "1";

    /// <summary>Single-character text for the first letter (e.g. "A").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstLetterError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(IncrementorPreview))]
    private string _firstLetterText = "A";

    /// <summary>Campus choices including a leading "(none)" sentinel.</summary>
    public List<CampusOption> CampusOptions { get; }

    /// <summary>Currently selected campus option.</summary>
    [ObservableProperty] private CampusOption? _selectedCampus;

    /// <summary>Section-type choices including a leading "(none)" sentinel.</summary>
    public List<SectionTypeOption> SectionTypeOptions { get; }

    /// <summary>Currently selected section-type option.</summary>
    [ObservableProperty] private SectionTypeOption? _selectedSectionType;

    // ── Derived visibility ────────────────────────────────────────────────────

    /// <summary>True when number fields (first number, increment) should be shown.</summary>
    public bool ShowNumberFields => !UseLetters;

    /// <summary>True when the letter field (first letter) should be shown.</summary>
    public bool ShowLetterFields => UseLetters;

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>Error for the Name field, or null when valid.</summary>
    public string? ValidationError
    {
        get
        {
            var trimmed = Name.Trim();
            if (trimmed.Length == 0) return null;
            if (_nameExists(trimmed)) return $"\"{trimmed}\" already exists.";
            return null;
        }
    }

    /// <summary>Error for the First Number field, or null when valid.</summary>
    public string? FirstNumberError
    {
        get
        {
            if (UseLetters) return null;
            if (!int.TryParse(FirstNumberText, out int n) || n < 0) return "Must be a whole number ≥ 0.";
            return null;
        }
    }

    /// <summary>Error for the Increment field, or null when valid.</summary>
    public string? IncrementError
    {
        get
        {
            if (UseLetters) return null;
            if (!int.TryParse(IncrementText, out int n) || n < 1) return "Must be a whole number ≥ 1.";
            return null;
        }
    }

    /// <summary>Error for the First Letter field, or null when valid.</summary>
    public string? FirstLetterError
    {
        get
        {
            if (!UseLetters) return null;
            var s = FirstLetterText.Trim().ToUpper();
            if (s.Length != 1 || s[0] < 'A' || s[0] > 'Z') return "Must be a single letter A–Z.";
            return null;
        }
    }

    /// <summary>
    /// Preview of the first few codes the pattern would generate, shown beneath the fields.
    /// Returns an empty string when input is not yet valid.
    /// </summary>
    public string Preview
    {
        get
        {
            if (!TryBuildPreviewPattern(out var p)) return string.Empty;
            var codes = SectionCodeGenerator.GetPreviewCodes(p!, count: 3);
            return codes.Count > 0 ? string.Join(",  ", codes) + ",  …" : string.Empty;
        }
    }

    /// <summary>
    /// Preview of just the incrementing values (no prefix/suffix), shown in the Incrementor
    /// column header to make clear what the radio buttons and first/step fields produce.
    /// Returns "?" when inputs are not yet valid.
    /// </summary>
    public string IncrementorPreview
    {
        get
        {
            if (!TryBuildPreviewPattern(out var p)) return "?";
            var stripped = new SectionCodePattern
            {
                Prefix      = string.Empty,
                Suffix      = string.Empty,
                UseLetters  = p!.UseLetters,
                FirstNumber = p.FirstNumber,
                PadWidth    = p.PadWidth,
                Increment   = p.Increment,
                FirstLetter = p.FirstLetter,
            };
            var codes = SectionCodeGenerator.GetPreviewCodes(stripped, count: 3);
            return codes.Count > 0 ? string.Join("  ", codes) + "  …" : "?";
        }
    }

    private bool CanSave()
    {
        if (Name.Trim().Length == 0 || ValidationError is not null) return false;
        if (UseLetters)
            return FirstLetterError is null;
        return FirstNumberError is null && IncrementError is null;
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="target">The model object to populate on save.</param>
    /// <param name="isNew">True when adding; false when editing.</param>
    /// <param name="campusOptions">Campus choices including a leading "(none)" entry.</param>
    /// <param name="sectionTypeOptions">Section-type choices including a leading "(none)" entry.</param>
    /// <param name="onSave">Called with the updated model when the user saves.</param>
    /// <param name="onCancel">Called when the user cancels.</param>
    /// <param name="nameExists">
    /// Predicate returning true when the given name is already taken
    /// (scoped to exclude the current record when editing).
    /// </param>
    public SectionCodePatternEditViewModel(
        SectionCodePattern target,
        bool isNew,
        List<CampusOption> campusOptions,
        List<SectionTypeOption> sectionTypeOptions,
        Action<SectionCodePattern> onSave,
        Action onCancel,
        Func<string, bool> nameExists)
    {
        _target    = target;
        IsNew      = isNew;
        _onSave    = onSave;
        _onCancel  = onCancel;
        _nameExists = nameExists;

        CampusOptions      = campusOptions;
        SectionTypeOptions = sectionTypeOptions;

        Name          = target.Name;
        Prefix        = target.Prefix;
        Suffix        = target.Suffix;
        UseLetters    = target.UseLetters;
        FirstLetterText = target.FirstLetter.ToString();

        if (target.UseLetters)
        {
            FirstNumberText = "1";
            IncrementText   = "1";
        }
        else
        {
            // Reconstruct the padded first-number string so pad width is preserved on edit.
            FirstNumberText = target.PadWidth > 0
                ? target.FirstNumber.ToString().PadLeft(target.PadWidth, '0')
                : target.FirstNumber.ToString();
            IncrementText = target.Increment.ToString();
        }

        SelectedCampus      = campusOptions.FirstOrDefault(c => c.Id == target.CampusId) ?? campusOptions[0];
        SelectedSectionType = sectionTypeOptions.FirstOrDefault(t => t.Id == target.SectionTypeId) ?? sectionTypeOptions[0];
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Writes field values back to the model and invokes the save callback.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _target.Name          = Name.Trim();
        _target.Prefix        = Prefix;
        _target.Suffix        = Suffix;
        _target.UseLetters    = UseLetters;
        _target.CampusId      = SelectedCampus?.Id;
        _target.SectionTypeId = SelectedSectionType?.Id;

        if (UseLetters)
        {
            _target.FirstLetter = char.ToUpper(FirstLetterText.Trim()[0]);
        }
        else
        {
            // Detect pad width from the leading-zero length of FirstNumberText.
            int.TryParse(FirstNumberText, out int firstNum);
            int padWidth = FirstNumberText.Length > firstNum.ToString().Length
                ? FirstNumberText.Length : 0;

            int.TryParse(IncrementText, out int increment);

            _target.FirstNumber = firstNum;
            _target.PadWidth    = padWidth;
            _target.Increment   = increment;
        }

        _onSave(_target);
    }

    /// <summary>Discards changes and invokes the cancel callback.</summary>
    [RelayCommand]
    private void Cancel() => _onCancel();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to build a preview-only <see cref="SectionCodePattern"/> from the current
    /// field values. Returns false when the inputs are not yet valid.
    /// </summary>
    private bool TryBuildPreviewPattern(out SectionCodePattern? pattern)
    {
        pattern = null;

        if (UseLetters)
        {
            var s = FirstLetterText.Trim().ToUpper();
            if (s.Length != 1 || s[0] < 'A' || s[0] > 'Z') return false;

            pattern = new SectionCodePattern
            {
                Prefix     = Prefix,
                Suffix     = Suffix,
                UseLetters = true,
                FirstLetter = s[0],
            };
            return true;
        }
        else
        {
            if (!int.TryParse(FirstNumberText, out int firstNum) || firstNum < 0) return false;
            if (!int.TryParse(IncrementText,  out int increment) || increment < 1) return false;

            int padWidth = FirstNumberText.Length > firstNum.ToString().Length
                ? FirstNumberText.Length : 0;

            pattern = new SectionCodePattern
            {
                Prefix     = Prefix,
                Suffix     = Suffix,
                UseLetters = false,
                FirstNumber = firstNum,
                PadWidth    = padWidth,
                Increment   = increment,
            };
            return true;
        }
    }
}
