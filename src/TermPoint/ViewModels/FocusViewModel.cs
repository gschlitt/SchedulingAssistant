using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels;

/// <summary>
/// One checkable row of the Focus picker: a subject and whether its box is ticked. Created by
/// <see cref="FocusViewModel"/> each time the picker opens.
/// </summary>
public sealed partial class FocusSubjectItem : ObservableObject
{
    /// <summary>The subject's id; what goes into the <see cref="SubjectFocus"/>.</summary>
    public string SubjectId { get; }

    /// <summary>The short name used in the indicator: the calendar abbreviation, or the name when that is blank.</summary>
    public string Label { get; }

    /// <summary>The text shown beside the checkbox: "ABBR - Name", or just the name when there is no abbreviation.</summary>
    public string DisplayText { get; }

    /// <summary>Whether the subject's box is ticked. Two-way bound to the checkbox.</summary>
    [ObservableProperty] private bool _isTicked;

    /// <summary>Creates a row for <paramref name="subject"/>.</summary>
    /// <param name="subject">The subject this row stands for.</param>
    /// <param name="isTicked">Whether the box starts ticked.</param>
    public FocusSubjectItem(Subject subject, bool isTicked)
    {
        SubjectId = subject.Id;
        Label = FocusViewModel.LabelOf(subject);
        DisplayText = string.IsNullOrWhiteSpace(subject.CalendarAbbreviation)
            ? subject.Name
            : $"{subject.CalendarAbbreviation} - {subject.Name}";
        _isTicked = isTicked;
    }
}

/// <summary>
/// The view model behind every place that shows the subject focus (spec item 20): the reusable focus statement
/// ("Focus: BIOL, CHEM" with a Show all link, shown prominently in the Section View and Schedule View headers) and the Focus picker (a checklist of subjects with
/// All, None, Apply and Cancel). A singleton shared by all of them, built over <see cref="FocusService"/> and
/// <see cref="ISubjectRepository"/>, and exposed as <c>MainWindowViewModel.FocusVm</c>.
/// <para>
/// <b>Rule 3.</b> Every command that changes the focus has <c>CanExecute = FocusService.CanChangeFocus</c> and
/// re-evaluates when that changes, so no control here can change the focus while a section editor is open. (The
/// service also refuses the change itself; the commands being disabled is just how the UI shows it.)
/// </para>
/// </summary>
public sealed partial class FocusViewModel : ObservableObject, IDisposable
{
    /// <summary>The focus statement under None (and under a Subjects focus whose subjects have all gone).</summary>
    public const string NoSubjectsText = "Focus: no subjects";

    /// <summary>Tooltip of a focus-changing control while an editor is open.</summary>
    public const string FinishEditingFirst = "Finish editing first";

    private readonly FocusService _focus;
    private readonly ISubjectRepository _subjectRepo;

    /// <summary>Whether the picker popup is open. Two-way bound to the popup, so a light dismiss closes it.</summary>
    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>
    /// Creates the view model, builds the indicator text, and subscribes to focus changes and to
    /// <see cref="FocusService.CanChangeFocus"/> changes (to re-evaluate the commands).
    /// </summary>
    /// <param name="focus">The focus service.</param>
    /// <param name="subjectRepo">Subjects, for labels, the total count and the picker's rows.</param>
    public FocusViewModel(FocusService focus, ISubjectRepository subjectRepo)
    {
        _focus = focus;
        _subjectRepo = subjectRepo;

        _focus.FocusChanged += OnFocusChanged;
        _focus.PropertyChanged += OnFocusServicePropertyChanged;

        RefreshIndicator();
        RefreshSubjectsLabel();
    }

    // ── Indicator ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The focus statement: "Focus: BIOL, CHEM" under a Subjects focus (labels sorted; a blank abbreviation falls
    /// back to the subject's name), "Focus: no subjects" under None, and empty under All (where the statement is
    /// hidden anyway). It carries no "N of M subjects" count: the labels are the point, and a count would go stale
    /// whenever subjects are added or removed.
    /// </summary>
    [ObservableProperty] private string _indicatorText = string.Empty;

    /// <summary>False under All (nothing is hidden, so there is nothing to indicate); true under None and Subjects.</summary>
    [ObservableProperty] private bool _isIndicatorVisible;

    /// <summary>
    /// The current focus, for tests and any view that needs it. Read-only: change it through the commands or
    /// <see cref="FocusService.SetFocus"/>.
    /// </summary>
    public SubjectFocus CurrentFocus => _focus.Current;

    /// <summary>Whether a focus change is allowed right now (false while a section editor is open).</summary>
    public bool CanChangeFocus => _focus.CanChangeFocus;

    /// <summary>
    /// The tooltip for a focus-changing button: "Finish editing first" while an editor is open, otherwise a short
    /// description of what the control does. Bind with <c>ToolTip.ShowOnDisabled</c>, since the tip matters most
    /// when the control is disabled.
    /// </summary>
    public string ChangeFocusToolTip => CanChangeFocus ? "Choose which subjects to work on" : FinishEditingFirst;

    /// <summary>The top-bar Subjects button's text under All.</summary>
    public const string SubjectsAllText = "All";

    /// <summary>The top-bar Subjects button's text under None (nothing chosen yet).</summary>
    public const string SubjectsNoneText = "Choose…";

    /// <summary>
    /// The text of the top-bar Subjects button, a peer of the Academic Year and Semester pickers: "All" under All,
    /// "Choose…" under None, and the sorted labels ("BIOL, CHEM") under a Subjects focus (a blank abbreviation
    /// falls back to the subject's name). Always non-empty, unlike <see cref="IndicatorText"/>.
    /// </summary>
    [ObservableProperty] private string _subjectsLabel = SubjectsAllText;

    /// <summary>
    /// The tooltip of the top-bar Subjects button: "Finish editing first" while an editor is open, otherwise the
    /// full list of chosen subjects (the button itself may trim a long list) or the generic description.
    /// </summary>
    public string SubjectsToolTip => !CanChangeFocus
        ? FinishEditingFirst
        : _focus.Current.Kind == FocusKind.Subjects ? $"Subjects: {SubjectsLabel}" : ChangeFocusToolTip;

    /// <summary>
    /// Rebuilds <see cref="SubjectsLabel"/> from the current focus and the subjects in the database. A subject list
    /// that cannot be read is logged and leaves a generic text.
    /// </summary>
    private void RefreshSubjectsLabel()
    {
        var focus = _focus.Current;
        switch (focus.Kind)
        {
            case FocusKind.All:
                SubjectsLabel = SubjectsAllText;
                break;
            case FocusKind.None:
                SubjectsLabel = SubjectsNoneText;
                break;
            default:
                try
                {
                    // Only subjects that still exist are named; a deleted one is pruned by the service.
                    var labels = _subjectRepo.GetAll()
                        .Where(s => focus.ContainsSubject(s.Id))
                        .Select(LabelOf)
                        .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    SubjectsLabel = labels.Count == 0 ? SubjectsNoneText : string.Join(", ", labels);
                }
                catch (Exception ex)
                {
                    App.Logger.LogInfo($"[FocusViewModel] Could not read subjects for the label: {ex.Message}");
                    SubjectsLabel = "Some subjects";
                }
                break;
        }
        OnPropertyChanged(nameof(SubjectsToolTip));
    }

    /// <summary>The short label of a subject: its calendar abbreviation, or its name when the abbreviation is blank.</summary>
    /// <param name="subject">The subject.</param>
    /// <returns>The label.</returns>
    internal static string LabelOf(Subject subject) =>
        string.IsNullOrWhiteSpace(subject.CalendarAbbreviation) ? subject.Name : subject.CalendarAbbreviation;

    /// <summary>
    /// Rebuilds <see cref="IndicatorText"/> and <see cref="IsIndicatorVisible"/> from the current focus and the
    /// subjects in the database. A subject list that cannot be read is logged and leaves a generic text.
    /// </summary>
    private void RefreshIndicator()
    {
        var focus = _focus.Current;
        IsIndicatorVisible = focus.Kind != FocusKind.All;

        switch (focus.Kind)
        {
            case FocusKind.All:
                IndicatorText = string.Empty;
                return;
            case FocusKind.None:
                IndicatorText = NoSubjectsText;
                return;
        }

        List<Subject> subjects;
        try
        {
            subjects = _subjectRepo.GetAll();
        }
        catch (Exception ex)
        {
            App.Logger.LogInfo($"[FocusViewModel] Could not read subjects for the indicator: {ex.Message}");
            IndicatorText = "Focus: some subjects";
            return;
        }

        // Only subjects that still exist are named; a deleted one is pruned by the service and never shown.
        var labels = subjects
            .Where(s => focus.ContainsSubject(s.Id))
            .Select(LabelOf)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IndicatorText = labels.Count == 0
            ? NoSubjectsText
            : $"Focus: {string.Join(", ", labels)}";
    }

    /// <summary>Sets the focus to All (the indicator's Show all link). Disabled while an editor is open.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeFocus))]
    private void ShowAll() => _focus.SetFocus(SubjectFocus.All);

    /// <summary>Sets the focus to All and closes the picker (the picker's and the empty state's All button).</summary>
    [RelayCommand(CanExecute = nameof(CanChangeFocus))]
    private void All()
    {
        _focus.SetFocus(SubjectFocus.All);
        IsPickerOpen = false;
    }

    /// <summary>Sets the focus to None and closes the picker.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeFocus))]
    private void None()
    {
        _focus.SetFocus(SubjectFocus.None);
        IsPickerOpen = false;
    }

    // ── Picker ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// One checkable row per subject, sorted by abbreviation, rebuilt each time the picker opens (subjects may have
    /// been added or removed since) and pre-ticked from the current focus. Empty until the picker is first opened.
    /// </summary>
    public ObservableCollection<FocusSubjectItem> SubjectItems { get; } = new();

    /// <summary>
    /// Builds the picker rows (ticked from the current focus: every box under All, none under None) and opens the
    /// popup. Disabled while an editor is open.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeFocus))]
    private void OpenPicker()
    {
        RebuildSubjectItems();
        IsPickerOpen = true;
    }

    /// <summary>
    /// Applies the ticks: every subject ticked (or no subjects at all) gives All, none ticked gives None, otherwise
    /// a Subjects focus on the ticked ones. Closes the picker. Disabled while an editor is open.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeFocus))]
    private void Apply()
    {
        var ticked = SubjectItems.Where(i => i.IsTicked).Select(i => i.SubjectId).ToList();

        var focus = ticked.Count == SubjectItems.Count ? SubjectFocus.All
                  : ticked.Count == 0 ? SubjectFocus.None
                  : SubjectFocus.ForSubjects(ticked);

        _focus.SetFocus(focus);
        IsPickerOpen = false;
    }

    /// <summary>Closes the picker without changing the focus.</summary>
    [RelayCommand]
    private void Cancel() => IsPickerOpen = false;

    /// <summary>Replaces <see cref="SubjectItems"/> with one row per subject, ticked from the current focus.</summary>
    private void RebuildSubjectItems()
    {
        var focus = _focus.Current;
        var subjects = _subjectRepo.GetAll()
            .OrderBy(LabelOf, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

        SubjectItems.Clear();
        foreach (var subject in subjects)
        {
            bool ticked = focus.Kind switch
            {
                FocusKind.All => true,
                FocusKind.None => false,
                _ => focus.ContainsSubject(subject.Id),
            };
            SubjectItems.Add(new FocusSubjectItem(subject, ticked));
        }
    }

    // ── Change notifications ───────────────────────────────────────────────────

    /// <summary>The focus changed: refresh the indicator and the focus-dependent state.</summary>
    private void OnFocusChanged()
    {
        RefreshIndicator();
        RefreshSubjectsLabel();
        OnPropertyChanged(nameof(CurrentFocus));
    }

    /// <summary>
    /// Re-evaluates every focus-changing command (and the tooltip) when the service's
    /// <see cref="FocusService.CanChangeFocus"/> flips, which is when a section editor opens or closes.
    /// </summary>
    private void OnFocusServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusService.CanChangeFocus)) return;

        OnPropertyChanged(nameof(CanChangeFocus));
        OnPropertyChanged(nameof(ChangeFocusToolTip));
        OnPropertyChanged(nameof(SubjectsToolTip));
        ShowAllCommand.NotifyCanExecuteChanged();
        AllCommand.NotifyCanExecuteChanged();
        NoneCommand.NotifyCanExecuteChanged();
        OpenPickerCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Unsubscribes from the focus service. Safe to call more than once.</summary>
    public void Dispose()
    {
        _focus.FocusChanged -= OnFocusChanged;
        _focus.PropertyChanged -= OnFocusServicePropertyChanged;
    }
}
