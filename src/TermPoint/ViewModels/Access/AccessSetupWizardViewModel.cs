using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.ViewModels.Management;
using TermPoint.Services;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>Where the access setup wizard is.</summary>
public enum AccessWizardStep
{
    /// <summary>Question 1: do any courses need more than one section?</summary>
    Question,
    /// <summary>Question 2: which section types are components?</summary>
    Types,
    /// <summary>The course check: every course's components, pre-filled.</summary>
    Courses,
    /// <summary>"Set up your section types first": the wizard ends with nothing saved.</summary>
    TypesFirst,
    /// <summary>"New type" mode: which courses have the section type that was just marked?</summary>
    NewType,
}

/// <summary>One section type's box in step 2.</summary>
public sealed partial class WizardTypeRow : ObservableObject
{
    /// <summary>The section type id.</summary>
    public string Id { get; }

    /// <summary>The section type's name.</summary>
    public string Name { get; }

    /// <summary>Whether the type is ticked as a component.</summary>
    [ObservableProperty] private bool _isTicked;

    /// <summary>Creates a row.</summary>
    /// <param name="id">The section type id.</param>
    /// <param name="name">The section type's name.</param>
    /// <param name="ticked">Whether it starts ticked.</param>
    public WizardTypeRow(string id, string name, bool ticked)
    {
        Id = id;
        Name = name;
        _isTicked = ticked;
    }
}

/// <summary>One course x section type box in the course table.</summary>
public sealed partial class WizardCell : ObservableObject
{
    /// <summary>The section type id of this column.</summary>
    public string TypeId { get; }

    /// <summary>Whether the course has this type as a component.</summary>
    [ObservableProperty] private bool _isTicked;

    /// <summary>Creates a cell.</summary>
    /// <param name="typeId">The column's section type id.</param>
    /// <param name="ticked">Whether it starts ticked.</param>
    public WizardCell(string typeId, bool ticked)
    {
        TypeId = typeId;
        _isTicked = ticked;
    }
}

/// <summary>One course's row in the course table: its code, one cell per column, and the "any one section" hint.</summary>
public sealed partial class WizardCourseRow : ObservableObject
{
    /// <summary>The course id.</summary>
    public string CourseId { get; }

    /// <summary>The course's calendar code (e.g. "BIOL 201").</summary>
    public string Code { get; }

    /// <summary>The boxes, one per column of the table, in column order.</summary>
    public IReadOnlyList<WizardCell> Cells { get; }

    /// <summary>True when the row is highlighted (new-type mode: the course has sections of the new type).</summary>
    public bool IsHighlighted { get; }

    /// <summary>Component type ids the course stores that have no column here (kept when saving).</summary>
    public IReadOnlyList<string> Preserved { get; }

    /// <summary>The ticked component type ids plus <see cref="Preserved"/>, as stored.</summary>
    public IReadOnlyList<string> TickedTypeIds =>
        Cells.Where(c => c.IsTicked).Select(c => c.TypeId).Concat(Preserved).ToList();

    /// <summary>Creates a row.</summary>
    /// <param name="courseId">The course id.</param>
    /// <param name="code">The calendar code.</param>
    /// <param name="cells">The boxes.</param>
    /// <param name="highlighted">Whether the row is highlighted.</param>
    /// <param name="preserved">Stored ids with no column, kept on save.</param>
    public WizardCourseRow(string courseId, string code, IReadOnlyList<WizardCell> cells, bool highlighted, IReadOnlyList<string> preserved)
    {
        CourseId = courseId;
        Code = code;
        Cells = cells;
        IsHighlighted = highlighted;
        Preserved = preserved;
    }
}

/// <summary>
/// The access setup wizard (revision 8f), shown as a flyout page. Two modes: the <b>setup</b> mode (question 1, the
/// component section types, the course check) and the <b>new type</b> mode (after a section type is marked
/// Component: which courses have it?). All wording is in <see cref="AccessText"/> (the r9 wireframe's
/// <c>wizardHtml</c>). Every write goes through <see cref="AccessSetupService"/>, one transaction each; Cancel and
/// Skip save nothing. A failed save keeps the wizard open and shows the failure in
/// <see cref="ViewModelBase.LastErrorMessage"/> (nothing was written).
/// </summary>
public sealed partial class AccessSetupWizardViewModel : ViewModelBase
{
    private readonly AccessSetupService _setup;
    private readonly ISchedulingEnvironmentRepository _envRepo;
    private readonly ICourseRepository _courseRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly Action _close;
    private readonly Action? _onFinished;
    private readonly string? _newTypeId;

    /// <summary>Courses' ticks as the user left them, kept so Back and Next again do not lose edits.</summary>
    private readonly Dictionary<string, HashSet<string>> _edited = new();

    /// <summary>Where the wizard is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsQuestionStep), nameof(IsTypesStep), nameof(IsCoursesStep),
        nameof(IsTypesFirstStep), nameof(IsNewTypeStep), nameof(IsCourseTable), nameof(ShowNext), nameof(ShowFinish),
        nameof(ShowBack), nameof(ShowTheseAreRight), nameof(ShowSave), nameof(ShowSkip), nameof(ShowCancel), nameof(ShowClose),
        nameof(CourseTableText), nameof(NextDisabledTip))]
    private AccessWizardStep _step;

    /// <summary>The section types shown in step 2.</summary>
    public ObservableCollection<WizardTypeRow> TypeRows { get; } = new();

    /// <summary>The course table's column headings (the component types in play), in column order.</summary>
    [ObservableProperty] private IReadOnlyList<string> _columnHeadings = [];

    /// <summary>The course table's rows: every course, sorted by calendar code.</summary>
    [ObservableProperty] private IReadOnlyList<WizardCourseRow> _courseRows = [];

    private bool _isYes;
    private bool _isNo;

    /// <summary>Question 1's Yes radio (two-way). Choosing it shows Next.</summary>
    public bool IsYes
    {
        get => _isYes;
        set
        {
            if (_isYes == value) return;
            _isYes = value;
            if (value) _isNo = false;
            AnswerChanged();
        }
    }

    /// <summary>Question 1's No radio (two-way). Choosing it shows Finish.</summary>
    public bool IsNo
    {
        get => _isNo;
        set
        {
            if (_isNo == value) return;
            _isNo = value;
            if (value) _isYes = false;
            AnswerChanged();
        }
    }

    /// <summary>The section type that was just marked (new-type mode); null in setup mode.</summary>
    public string? NewTypeName { get; }

    /// <summary>True in new-type mode.</summary>
    public bool IsNewTypeMode => _newTypeId is not null;

    /// <summary>The current step's title.</summary>
    public string Title => Step switch
    {
        AccessWizardStep.Question => AccessText.WizardStep1Title,
        AccessWizardStep.Types => AccessText.WizardStep2Title,
        AccessWizardStep.TypesFirst => AccessText.WizardTypesFirstTitle,
        AccessWizardStep.NewType => AccessText.WizardNewTypeTitle(NewTypeName ?? string.Empty),
        _ => AccessText.WizardCoursesTitle,
    };

    /// <summary>Step 1's question.</summary>
    public string Question => AccessText.WizardQuestion;
    /// <summary>Step 1's example paragraph.</summary>
    public string Example => AccessText.WizardExample;
    /// <summary>Step 1's note on parallel courses.</summary>
    public string ParallelCourses => AccessText.WizardParallelCourses;
    /// <summary>The Yes radio's label.</summary>
    public string YesLabel => AccessText.WizardYesLabel;
    /// <summary>The No radio's label.</summary>
    public string NoLabel => AccessText.WizardNoLabel;
    /// <summary>Step 2's question.</summary>
    public string TypesQuestion => AccessText.WizardTypesQuestion;
    /// <summary>Step 2's explanation.</summary>
    public string TypesText => AccessText.WizardTypesText;
    /// <summary>The link under step 2's boxes.</summary>
    public string TypesFirstLinkText => AccessText.WizardTypesFirstLink;
    /// <summary>The text of the "types first" step.</summary>
    public string TypesFirstText => AccessText.WizardTypesFirstText;
    /// <summary>The note under the course table.</summary>
    public string CoursesFootnote => AccessText.WizardCoursesFootnote;
    /// <summary>The second button of new-type mode.</summary>
    public string SkipText => AccessText.WizardSkipForNow;

    /// <summary>The text above the course table for the current mode.</summary>
    public string CourseTableText => Step == AccessWizardStep.NewType
        ? AccessText.WizardNewTypeText(NewTypeName ?? string.Empty)
        : AccessText.WizardCoursesText;

    /// <summary>The tooltip of the disabled Next button on step 2 (empty when Next is enabled).</summary>
    public string NextDisabledTip => Step == AccessWizardStep.Types && !TypeRows.Any(t => t.IsTicked)
        ? AccessText.WizardNextDisabledTip : string.Empty;

    /// <summary>True on question 1.</summary>
    public bool IsQuestionStep => Step == AccessWizardStep.Question;
    /// <summary>True on question 2.</summary>
    public bool IsTypesStep => Step == AccessWizardStep.Types;
    /// <summary>True on the setup mode's course check.</summary>
    public bool IsCoursesStep => Step == AccessWizardStep.Courses;
    /// <summary>True on the "types first" step.</summary>
    public bool IsTypesFirstStep => Step == AccessWizardStep.TypesFirst;
    /// <summary>True in new-type mode.</summary>
    public bool IsNewTypeStep => Step == AccessWizardStep.NewType;
    /// <summary>True when the course table is showing (either mode).</summary>
    public bool IsCourseTable => Step is AccessWizardStep.Courses or AccessWizardStep.NewType;

    /// <summary>Next is offered on question 1 (answer Yes) and on question 2.</summary>
    public bool ShowNext => (Step == AccessWizardStep.Question && !_isNo) || Step == AccessWizardStep.Types;
    /// <summary>Finish replaces Next when question 1 is answered No.</summary>
    public bool ShowFinish => Step == AccessWizardStep.Question && _isNo;
    /// <summary>Back is offered on question 2 and the course check.</summary>
    public bool ShowBack => Step is AccessWizardStep.Types or AccessWizardStep.Courses;
    /// <summary>"These are right" ends the setup mode's course check.</summary>
    public bool ShowTheseAreRight => Step == AccessWizardStep.Courses;
    /// <summary>Save ends new-type mode.</summary>
    public bool ShowSave => Step == AccessWizardStep.NewType;
    /// <summary>"Skip for now" is new-type mode's second button.</summary>
    public bool ShowSkip => Step == AccessWizardStep.NewType;
    /// <summary>Cancel is on every step except the "types first" step and new-type mode.</summary>
    public bool ShowCancel => Step is AccessWizardStep.Question or AccessWizardStep.Types or AccessWizardStep.Courses;
    /// <summary>Close is the only button of the "types first" step.</summary>
    public bool ShowClose => Step == AccessWizardStep.TypesFirst;

    /// <summary>Creates the wizard and fills it for its mode.</summary>
    /// <param name="setup">Saves the answers.</param>
    /// <param name="envRepo">Reads the section types.</param>
    /// <param name="courseRepo">Reads the courses.</param>
    /// <param name="sectionRepo">Reads which section types each course's sections have (the pre-fill).</param>
    /// <param name="close">Closes the flyout page (Cancel, Skip, Close, and after a successful save).</param>
    /// <param name="onFinished">Called after a successful save (for example turns on the watch that was waiting), or null.</param>
    /// <param name="newTypeId">The section type just marked as a component, for new-type mode; null for the setup mode.</param>
    public AccessSetupWizardViewModel(
        AccessSetupService setup, ISchedulingEnvironmentRepository envRepo, ICourseRepository courseRepo,
        ISectionRepository sectionRepo, Action close, Action? onFinished = null, string? newTypeId = null)
    {
        _setup = setup;
        _envRepo = envRepo;
        _courseRepo = courseRepo;
        _sectionRepo = sectionRepo;
        _close = close;
        _onFinished = onFinished;
        _newTypeId = newTypeId;

        var types = SectionTypesInOrder();
        if (newTypeId is not null)
        {
            NewTypeName = types.FirstOrDefault(t => t.Id == newTypeId)?.Name ?? string.Empty;
            BuildNewTypeTable(types);
            Step = AccessWizardStep.NewType;
            return;
        }

        var pre = ComponentSuggestion.PreTickedTypes(types);
        foreach (var t in types)
        {
            var row = new WizardTypeRow(t.Id, t.Name, pre.Contains(t.Id));
            row.PropertyChanged += (_, _) => OnTypesTickChanged();
            TypeRows.Add(row);
        }
        Step = AccessWizardStep.Question;
    }

    /// <summary>The section types in section type order.</summary>
    private List<SchedulingEnvironmentValue> SectionTypesInOrder() =>
        _envRepo.GetAll(SchedulingEnvironmentTypes.SectionType).OrderBy(t => t.SortOrder).ToList();

    /// <summary>Re-reads the buttons after an answer changed (the radios notify through the buttons' flags).</summary>
    private void AnswerChanged()
    {
        OnPropertyChanged(nameof(IsYes));
        OnPropertyChanged(nameof(IsNo));
        OnPropertyChanged(nameof(ShowNext));
        OnPropertyChanged(nameof(ShowFinish));
        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-evaluates Next when a type box changes (Next needs at least one ticked type).</summary>
    private void OnTypesTickChanged()
    {
        NextCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(NextDisabledTip));
    }

    /// <summary>Whether Next can run: an answer of Yes on question 1, at least one ticked type on question 2.</summary>
    private bool CanNext() => Step switch
    {
        AccessWizardStep.Question => _isYes,
        AccessWizardStep.Types => TypeRows.Any(t => t.IsTicked),
        _ => false,
    };

    /// <summary>
    /// Goes on: question 1 (Yes) to question 2, or straight to the "types first" step when there are no section
    /// types; question 2 to the course check, pre-filled from the chosen types.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next()
    {
        LastErrorMessage = null;
        if (Step == AccessWizardStep.Question)
        {
            Step = TypeRows.Count == 0 ? AccessWizardStep.TypesFirst : AccessWizardStep.Types;
        }
        else if (Step == AccessWizardStep.Types)
        {
            BuildSetupTable();
            Step = AccessWizardStep.Courses;
        }
        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Goes back one step; the course check remembers the ticks the user made.</summary>
    [RelayCommand]
    private void Back()
    {
        LastErrorMessage = null;
        if (Step == AccessWizardStep.Courses)
        {
            RememberEdits();
            Step = AccessWizardStep.Types;
        }
        else if (Step == AccessWizardStep.Types)
        {
            Step = AccessWizardStep.Question;
        }
        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"My section types need revising first…": ends the wizard on the "types first" step, nothing saved.</summary>
    [RelayCommand]
    private void TypesFirst()
    {
        LastErrorMessage = null;
        Step = AccessWizardStep.TypesFirst;
    }

    /// <summary>Cancel, Skip for now and Close: saves nothing and closes the wizard.</summary>
    [RelayCommand]
    private void Cancel() => _close();

    /// <summary>The answer No: unmarks every section type, writes the set-up mark, then finishes.</summary>
    [RelayCommand]
    private void Finish() => TrySave(() => _setup.SaveNoComponents());

    /// <summary>"These are right": saves the chosen types, every course's components and the set-up mark in one transaction.</summary>
    [RelayCommand]
    private void TheseAreRight()
    {
        var chosen = TypeRows.Where(t => t.IsTicked).Select(t => t.Id).ToHashSet();
        var byCourse = CourseRows.ToDictionary(r => r.CourseId, r => r.TickedTypeIds);
        TrySave(() => _setup.SaveSetup(chosen, byCourse));
    }

    /// <summary>Save, in new-type mode: stores the ticked components of every course in one transaction.</summary>
    [RelayCommand]
    private void Save()
    {
        var byCourse = CourseRows.ToDictionary(r => r.CourseId, r => r.TickedTypeIds);
        TrySave(() => _setup.SaveCourseComponents(byCourse));
    }

    /// <summary>
    /// Runs a save. On success calls <c>onFinished</c> and closes; on failure logs, shows the failure and stays open
    /// (nothing was written, so the user can try again).
    /// </summary>
    /// <param name="save">The save to run.</param>
    private void TrySave(Action save)
    {
        LastErrorMessage = null;
        try
        {
            save();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessSetupWizardViewModel.Save");
            LastErrorMessage = WriteFailureText.For(ex, AccessText.WizardSaveFailed);
            return;
        }
        _onFinished?.Invoke();
        _close();
    }

    /// <summary>Keeps the course check's ticks so a Back and Next again does not reset them.</summary>
    private void RememberEdits()
    {
        foreach (var r in CourseRows)
            _edited[r.CourseId] = r.Cells.Where(c => c.IsTicked).Select(c => c.TypeId).ToHashSet();
    }

    /// <summary>
    /// Builds the setup mode's course table: columns are the chosen types; every course, sorted by calendar code, is
    /// pre-filled from its sections' types in every semester (or from the ticks the user made earlier).
    /// </summary>
    private void BuildSetupTable()
    {
        var types = SectionTypesInOrder();
        var chosen = TypeRows.Where(t => t.IsTicked).Select(t => t.Id).ToHashSet();
        var columns = types.Where(t => chosen.Contains(t.Id)).ToList();
        var seen = _sectionRepo.GetSectionTypeIdsByCourse();
        ColumnHeadings = columns.Select(t => t.Name).ToList();
        CourseRows = _courseRepo.GetAll()
            .OrderBy(c => c.CalendarCode, StringComparer.OrdinalIgnoreCase)
            .Select(c =>
            {
                var ticked = _edited.TryGetValue(c.Id, out var mine)
                    ? mine
                    : ComponentSuggestion.Prefill(c.Id, seen, chosen, types).ToHashSet();
                var cells = columns.Select(t => new WizardCell(t.Id, ticked.Contains(t.Id))).ToList();
                return new WizardCourseRow(c.Id, c.CalendarCode, cells, highlighted: false, preserved: []);
            })
            .ToList();
    }

    /// <summary>
    /// Builds new-type mode's table: columns are all the component types; every course is ticked from its stored
    /// list, plus the new type for courses that have sections of it (those rows are highlighted).
    /// </summary>
    /// <param name="types">The section types in order.</param>
    private void BuildNewTypeTable(IReadOnlyList<SchedulingEnvironmentValue> types)
    {
        var columns = types.Where(t => t.IsComponent || t.Id == _newTypeId).ToList();
        var columnIds = columns.Select(t => t.Id).ToHashSet();
        var seen = _sectionRepo.GetSectionTypeIdsByCourse();
        ColumnHeadings = columns.Select(t => t.Name).ToList();
        CourseRows = _courseRepo.GetAll()
            .OrderBy(c => c.CalendarCode, StringComparer.OrdinalIgnoreCase)
            .Select(c =>
            {
                bool has = seen.TryGetValue(c.Id, out var mine) && mine.Contains(_newTypeId!);
                var stored = c.ComponentTypeIds ?? [];
                var cells = columns
                    .Select(t => new WizardCell(t.Id, stored.Contains(t.Id) || (t.Id == _newTypeId && has)))
                    .ToList();
                return new WizardCourseRow(c.Id, c.CalendarCode, cells, highlighted: has,
                    preserved: stored.Where(id => !columnIds.Contains(id)).ToList());
            })
            .ToList();
    }
}
