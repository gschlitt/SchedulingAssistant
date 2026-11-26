using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.Services.Access;

namespace TermPoint.ViewModels.Access;

/// <summary>One problem line of Check all courses: a mark, a sentence, and a click that opens the course alone on the grid.</summary>
public sealed class AccessCheckProblemLineViewModel
{
    /// <summary>"▲".</summary>
    public string Mark => "▲";

    /// <summary>The sentence.</summary>
    public string Text { get; }

    /// <summary>The tooltip: "Show BMEG 230 on the grid".</summary>
    public string Tip { get; }

    /// <summary>Opens the course alone in the Access tab and on the grid (check mode).</summary>
    public IRelayCommand OpenCommand { get; }

    /// <summary>Creates a line.</summary>
    /// <param name="text">The sentence.</param>
    /// <param name="tip">The tooltip.</param>
    /// <param name="open">What a click does.</param>
    public AccessCheckProblemLineViewModel(string text, string tip, Action open)
    {
        Text = text;
        Tip = tip;
        OpenCommand = new RelayCommand(open);
    }
}

/// <summary>One quiet "worth a look" line of Check all courses: a sentence and one button (Show, or Open in Courses).</summary>
public sealed class AccessCheckQuietLineViewModel
{
    /// <summary>The sentence.</summary>
    public string Text { get; }

    /// <summary>The button's text: "Show" for a section, "Open in Courses" for a course.</summary>
    public string ActionText { get; }

    /// <summary>What the button does.</summary>
    public IRelayCommand ActionCommand { get; }

    /// <summary>Creates a line.</summary>
    /// <param name="text">The sentence.</param>
    /// <param name="actionText">The button's text.</param>
    /// <param name="action">What the button does.</param>
    public AccessCheckQuietLineViewModel(string text, string actionText, Action action)
    {
        Text = text;
        ActionText = actionText;
        ActionCommand = new RelayCommand(action);
    }
}

/// <summary>One semester's results in Check all courses: a headline, the problems, the quiet findings and a footer.</summary>
public sealed class AccessCheckSemesterViewModel
{
    /// <summary>The semester's name (shown as a heading only when several semesters are loaded).</summary>
    public string SemesterName { get; }

    /// <summary>True when the heading shows: more than one semester is loaded.</summary>
    public bool ShowName { get; }

    /// <summary>The headline: "2 courses need a look:", "No problems in 5 courses with more than one component." or the "Nothing to check" line.</summary>
    public string Headline { get; }

    /// <summary>True when the headline is drawn as needing attention (there are problems).</summary>
    public bool HeadlineIsAttention { get; }

    /// <summary>The problem lines, in course order.</summary>
    public IReadOnlyList<AccessCheckProblemLineViewModel> Problems { get; }

    /// <summary>True when there are quiet findings.</summary>
    public bool HasQuiet => Quiet.Count > 0;

    /// <summary>The heading of the quiet findings.</summary>
    public string QuietHeading => AccessText.CheckWorthALookText;

    /// <summary>The quiet findings, in course order.</summary>
    public IReadOnlyList<AccessCheckQuietLineViewModel> Quiet { get; }

    /// <summary>The footer: what was checked; empty for a semester with nothing to check.</summary>
    public string Footer { get; }

    /// <summary>Creates a semester's results.</summary>
    /// <param name="semesterName">The semester's name.</param>
    /// <param name="showName">Whether the name shows.</param>
    /// <param name="headline">The headline.</param>
    /// <param name="headlineIsAttention">Whether there are problems.</param>
    /// <param name="problems">The problem lines.</param>
    /// <param name="quiet">The quiet lines.</param>
    /// <param name="footer">The footer.</param>
    public AccessCheckSemesterViewModel(
        string semesterName, bool showName, string headline, bool headlineIsAttention,
        IReadOnlyList<AccessCheckProblemLineViewModel> problems, IReadOnlyList<AccessCheckQuietLineViewModel> quiet, string footer)
    {
        SemesterName = semesterName;
        ShowName = showName;
        Headline = headline;
        HeadlineIsAttention = headlineIsAttention;
        Problems = problems;
        Quiet = quiet;
        Footer = footer;
    }
}

/// <summary>
/// Check all courses in the Access tab (rules 39 to 41, round 9): every offered course of each loaded semester
/// analysed on its own (<see cref="CheckAllCourses.Run"/>), the results a snapshot that is never stored. A problem
/// line opens the course alone on the grid (check mode of <see cref="AccessPicture"/>); a quiet line shows the
/// section or opens the course in Courses. Singleton.
///
/// <para><b>Before the setup.</b> The check does not run on the suggestion (new watches don't either). Editing: the
/// button opens the setup wizard first and runs when it has saved (<see cref="AccessSetupService.RequestSetupWizard"/>).
/// Read-only: one quiet comment that someone who can edit needs to set up first. When the setup said no course has
/// components, the page says there is nothing to check.</para>
///
/// <para><b>Out of date.</b> Any <see cref="AccessState.Changed"/> after a run marks the results as from before the
/// last change; they stay on screen until Check again.</para>
/// </summary>
public sealed partial class AccessCheckViewModel : ViewModelBase
{
    private readonly AccessState _state;
    private readonly AccessPicture _picture;
    private readonly AccessSetupService _setup;
    private readonly SemesterContext _semesters;
    private readonly ICampusRepository _campusRepo;
    private readonly SectionStore _sectionStore;
    private List<(SemesterAccess Semester, CheckResult Result)>? _results;

    /// <summary>Raised when the page should be shown (after a run, or for a message that stands in for one).</summary>
    public event Action? ShowRequested;

    /// <summary>Raised when the user asks to go back to the list of watches.</summary>
    public event Action? BackRequested;

    /// <summary>Raised when the user asks to open a course in Courses; the main window opens the Courses flyout.</summary>
    public event Action<string>? OpenInCoursesRequested;

    /// <summary>The page's title.</summary>
    public string Title => AccessText.CheckAllCoursesText;

    /// <summary>The back button's text: "All watches" (the view draws the chevron).</summary>
    public string BackText => AccessText.BackToWatchesText;

    /// <summary>One block per loaded semester.</summary>
    public ObservableCollection<AccessCheckSemesterViewModel> Semesters { get; } = [];

    /// <summary>A message that stands in for the results: not set up, nothing to check; null when there are results.</summary>
    [ObservableProperty] private string? _notice;

    /// <summary>True when <see cref="Notice"/> is drawn quietly (the read-only comment).</summary>
    [ObservableProperty] private bool _noticeIsQuiet;

    /// <summary>True when the Set up button shows under <see cref="Notice"/> (not set up, and this computer can save).</summary>
    [ObservableProperty] private bool _showSetUpButton;

    /// <summary>The Set up button's text.</summary>
    public string SetUpButtonText => AccessText.CheckSetUpButtonText;

    /// <summary>True once the check has run (results or a "nothing to check" line are on screen).</summary>
    [ObservableProperty] private bool _hasRun;

    /// <summary>True when the results are from before the last change (the note shows).</summary>
    [ObservableProperty] private bool _isOutOfDate;

    /// <summary>The note over out-of-date results.</summary>
    public string OutOfDateText => AccessText.CheckOutOfDateText;

    /// <summary>True when the Check again button shows: the access setup is done.</summary>
    [ObservableProperty] private bool _canCheckAgain;

    /// <summary>The Check again button's text.</summary>
    public string CheckAgainText => AccessText.CheckAgainText;

    /// <summary>The first footer line (for the "to change that" hint); empty when there is none.</summary>
    [ObservableProperty] private string _footerLead = string.Empty;

    /// <summary>The last footer line: labs set up as separate courses (Q28).</summary>
    public string SeparateLabsText => AccessText.SeparateLabsText;

    /// <summary>Runs the check (or opens the setup wizard first); the Check all courses button of the list and Check again.</summary>
    public IRelayCommand RunCommand { get; }

    /// <summary>Opens the setup wizard, then runs the check (the Set up button under the not-set-up line).</summary>
    public IRelayCommand SetUpCommand { get; }

    /// <summary>Goes back to the list of watches.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>
    /// Creates the page.
    /// </summary>
    /// <param name="state">The access results the check reads; any change marks the results out of date.</param>
    /// <param name="picture">Where a problem line opens its course alone.</param>
    /// <param name="setup">Whether the access setup is done, whether this computer can save, and how to open the wizard.</param>
    /// <param name="semesters">The loaded semesters (their names).</param>
    /// <param name="campusRepo">Campus names for the sentences.</param>
    /// <param name="sectionStore">Where "Show" selects a section.</param>
    public AccessCheckViewModel(
        AccessState state, AccessPicture picture, AccessSetupService setup, SemesterContext semesters,
        ICampusRepository campusRepo, SectionStore sectionStore)
    {
        _state = state;
        _picture = picture;
        _setup = setup;
        _semesters = semesters;
        _campusRepo = campusRepo;
        _sectionStore = sectionStore;
        RunCommand = new RelayCommand(Run);
        SetUpCommand = new RelayCommand(Run, () => _setup.CanSave);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke());
        // Never unsubscribed: singletons that live as long as the services of one database.
        _state.Changed += OnStateChanged;
        _setup.CanSaveChanged += () => SetUpCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Any change marks results already on screen as out of date, and re-reads whether the setup is done.</summary>
    private void OnStateChanged()
    {
        if (HasRun && Notice is null) IsOutOfDate = true;
        CanCheckAgain = _setup.IsSetUp;
    }

    /// <summary>
    /// Runs the check. Before the setup: editing, opens the wizard and runs when it has saved; read-only, shows the
    /// quiet comment. After it, checks every offered course of every loaded semester on its own and shows the results.
    /// </summary>
    public void Run()
    {
        try
        {
            if (!_setup.IsSetUp)
            {
                if (_setup.CanSave)
                {
                    _setup.RequestSetupWizard(Run);
                    return;
                }
                ShowMessage(AccessText.CheckNotSetUpReadOnlyText, quiet: true, setUpButton: false);
                return;
            }
            RunCheck();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessCheckViewModel.Run");
            ShowMessage("The courses couldn't be checked. Your work is safe; try again.", quiet: false, setUpButton: false);
        }
    }

    /// <summary>Checks every loaded semester and builds the page.</summary>
    private void RunCheck()
    {
        var snapshot = _state.Current;
        _results = snapshot.Semesters.Values.Select(s => (s, CheckAllCourses.Run(s.Inputs))).ToList();
        IsOutOfDate = false;
        HasRun = true;
        CanCheckAgain = true;
        Semesters.Clear();
        Notice = null;
        NoticeIsQuiet = false;
        ShowSetUpButton = false;
        FooterLead = string.Empty;

        if (_results.Count == 0 || _results.All(r => r.Semester.Inputs.SectionTypes.All(t => !t.IsComponent)))
        {
            // The setup said no course takes more than one section: nothing can have a problem
            Notice = _results.Count == 0 ? AccessText.CheckNothingNoMultiText : AccessText.CheckNothingNoComponentsText;
            FooterLead = _results.Count == 0 ? string.Empty : AccessText.CheckChangeThatText;
            ShowRequested?.Invoke();
            return;
        }

        var campuses = _campusRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
        string CampusName(string? id) => id is not null && campuses.TryGetValue(id, out var n) ? n : "another campus";
        var names = _semesters.SelectedSemesters.ToDictionary(s => s.Semester.Id, s => s.Semester.Name);
        bool several = _results.Count > 1;
        foreach (var (semester, result) in _results)
            Semesters.Add(BuildSemester(semester, result, names.TryGetValue(semester.SemesterId, out var n) ? n : semester.SemesterId,
                several, CampusName));
        ShowRequested?.Invoke();
    }

    /// <summary>Builds one semester's block.</summary>
    private AccessCheckSemesterViewModel BuildSemester(
        SemesterAccess semester, CheckResult result, string name, bool showName, Func<string?, string> campusName)
    {
        var inputs = semester.Inputs;
        var quiet = result.Quiet.Select(q =>
        {
            string text = AccessText.CheckQuietLine(inputs, q);
            return q.Kind == QuietFindingKind.SectionNotCounted
                ? new AccessCheckQuietLineViewModel(text, AccessText.CheckShowText, () => _sectionStore.SetSelection(q.Sections[0].Id))
                : new AccessCheckQuietLineViewModel(text, AccessText.CheckOpenInCoursesText, () => OpenInCoursesRequested?.Invoke(q.CourseId));
        }).ToList();

        if (result.MultiComponentCourseCount == 0)
            return new AccessCheckSemesterViewModel(name, showName, AccessText.CheckNothingNoMultiText, false, [], quiet, string.Empty);

        var problems = result.Problems.Select(p => new AccessCheckProblemLineViewModel(
            AccessText.CheckProblemLine(inputs, p, campusName),
            AccessText.CheckProblemTip(inputs.CourseCode(p.CourseId)),
            () => _picture.OpenCourse(p.CourseId, semester.SemesterId))).ToList();
        int problemCourses = result.Problems.Select(p => p.CourseId).Distinct().Count();
        string headline = problemCourses > 0
            ? AccessText.CheckProblemsHeadline(problemCourses)
            : AccessText.CheckNoProblemsHeadline(result.MultiComponentCourseCount);
        return new AccessCheckSemesterViewModel(name, showName, headline, problemCourses > 0, problems, quiet,
            AccessText.CheckFooter(result.MultiComponentCourseCount, result.OfferedCourseCount));
    }

    /// <summary>Shows a message in place of results.</summary>
    private void ShowMessage(string text, bool quiet, bool setUpButton)
    {
        Semesters.Clear();
        _results = null;
        HasRun = true;
        IsOutOfDate = false;
        Notice = text;
        NoticeIsQuiet = quiet;
        ShowSetUpButton = setUpButton;
        FooterLead = string.Empty;
        CanCheckAgain = _setup.IsSetUp;
        ShowRequested?.Invoke();
    }
}
