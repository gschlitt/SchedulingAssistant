#if !BROWSER
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Data.Repositories;
using TermPoint.Services;
using TermPoint.Services.SectionImport;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// The stages of the Import Sections flyout. The view shows one panel per stage (read the matching
/// <c>Is...</c> property of <see cref="ImportSectionsViewModel"/>; no class toggling).
/// </summary>
public enum ImportStage
{
    /// <summary>
    /// Nothing chosen yet: the introduction, the target line and the four steps (Save template; the
    /// instructions, shown or saved; Choose file; what happens next).
    /// </summary>
    ReadyToChoose,

    /// <summary>The file is being read and checked ("Checking…").</summary>
    Checking,

    /// <summary>The file can't be imported yet: the list of problems.</summary>
    Problems,

    /// <summary>The file is good: the summary, the lists, the warnings, Import and Cancel.</summary>
    ReadyToImport,

    /// <summary>Every section in the file is already in the semester.</summary>
    NothingToAdd,

    /// <summary>The import was written.</summary>
    Done,
}

/// <summary>
/// The Import Sections flyout (Files, Import Sections…): explains the procedure, lets the user save the
/// template workbook, read the instructions (here in the flyout, or saved as a web page), choose a
/// filled-in <c>.xlsx</c> or <c>.csv</c>, see what importing it would do, and import it into the open
/// semester. All the work is done by <see cref="SectionImportService"/>; this class owns the stages, the
/// commands and the text of every message (<see cref="ImportMessages"/>).
///
/// <para><b>Dependencies are narrow on purpose.</b> What it needs from the main window comes through
/// <see cref="IImportSectionsHost"/> (write access, the shown semester, section editing, the reload after a
/// write); the file pickers, the clipboard and the bundled template come through
/// <see cref="IImportSectionsPlatform"/>. The real work sits in methods that take a path
/// (<see cref="CheckFileAsync"/>, <see cref="SaveTemplateToAsync"/>, <see cref="SaveInstructionsToAsync"/>), so
/// tests drive every stage without a window.</para>
///
/// <para><b>The four rules for the shared database.</b> The only write is
/// <see cref="SectionImportService.Import"/>, one transaction of repository inserts into the open copy; the
/// save, lock, checkout and recovery code is untouched (rules 1 to 3 as in the design, Phase 4). Rule 4: every
/// message says what happened, whether the user's work is safe and what to do, with no path, no exception text
/// and no "working copy". A failed write stays on Ready-to-import with the text <see cref="WriteFailureText"/>
/// chooses, and the details go to the log only.</para>
///
/// <para><b>Which semester.</b> The result on screen was checked against one semester. If the shown semester
/// changes while a result is on screen, the result is dropped (the user is told) rather than imported into a
/// semester it was not checked against.</para>
///
/// <para>Desktop only: it uses <see cref="SectionImportService"/>. Dispose it (the main window does when the
/// flyout closes) to stop listening to the host.</para>
/// </summary>
public partial class ImportSectionsViewModel : ViewModelBase, IDisposable
{
    private readonly SectionImportService _service;
    private readonly IImportSectionsHost _host;
    private readonly IImportSectionsPlatform _platform;
    private readonly FocusService _focus;
    private readonly ICourseRepository _courses;
    private readonly IAppLogger _logger;
    private readonly IDisposable _hostWatch;

    /// <summary>The file chosen last, re-read by Check again; null before a file has been chosen.</summary>
    private string? _lastPath;

    /// <summary>The semester the result on screen was checked against; null when no result is on screen.</summary>
    private ImportTarget? _checkedTarget;

    /// <summary>The parsed file of the result on screen, kept so Import re-plans the same rows.</summary>
    private ImportSheet? _sheet;

    /// <summary>The report the user is looking at; what Import compares the database against (decision D4).</summary>
    private ImportReport? _shownReport;

    /// <summary>The problems on screen, kept as records so Copy list can write them as columns.</summary>
    private IReadOnlyList<ImportProblem> _problems = Array.Empty<ImportProblem>();

    /// <summary>
    /// Creates the flyout's view model.
    /// </summary>
    /// <param name="service">Reads, checks and imports the file.</param>
    /// <param name="host">The main window's facts: write access, shown semester, section editing, reload.</param>
    /// <param name="platform">The pickers, the clipboard, the bundled template and the bundled instructions.</param>
    /// <param name="focus">The subject focus, for the "not shown" sentence after an import (decision D6).</param>
    /// <param name="courses">Read after a write to find which new sections are outside the chosen subjects.</param>
    /// <param name="logger">Where failures are logged (the app's logger).</param>
    public ImportSectionsViewModel(
        SectionImportService service,
        IImportSectionsHost host,
        IImportSectionsPlatform platform,
        FocusService focus,
        ICourseRepository courses,
        IAppLogger logger)
    {
        _service = service;
        _host = host;
        _platform = platform;
        _focus = focus;
        _courses = courses;
        _logger = logger;

        RefreshAvailability();
        _hostWatch = host.WatchChanges(OnHostChanged);
    }

    /// <summary>Stops listening to the host. The main window calls it when the flyout page is replaced.</summary>
    public void Dispose() => _hostWatch.Dispose();

    // ── Stage ─────────────────────────────────────────────────────────────────

    /// <summary>Where the flyout is. Changing it re-evaluates every command and every <c>Is...</c> stage property.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadyToChoose))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(IsProblems))]
    [NotifyPropertyChangedFor(nameof(IsReadyToImport))]
    [NotifyPropertyChangedFor(nameof(IsNothingToAdd))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private ImportStage _stage = ImportStage.ReadyToChoose;

    /// <summary>True in <see cref="ImportStage.ReadyToChoose"/>.</summary>
    public bool IsReadyToChoose => Stage == ImportStage.ReadyToChoose;

    /// <summary>True in <see cref="ImportStage.Checking"/>.</summary>
    public bool IsChecking => Stage == ImportStage.Checking;

    /// <summary>True in <see cref="ImportStage.Problems"/>.</summary>
    public bool IsProblems => Stage == ImportStage.Problems;

    /// <summary>True in <see cref="ImportStage.ReadyToImport"/>.</summary>
    public bool IsReadyToImport => Stage == ImportStage.ReadyToImport;

    /// <summary>True in <see cref="ImportStage.NothingToAdd"/>.</summary>
    public bool IsNothingToAdd => Stage == ImportStage.NothingToAdd;

    /// <summary>True in <see cref="ImportStage.Done"/>.</summary>
    public bool IsDone => Stage == ImportStage.Done;

    /// <summary>
    /// A status or error line belongs to the stage it was shown in, so it is cleared when the stage changes,
    /// and every command's enabled state is re-evaluated.
    /// </summary>
    /// <param name="value">The new stage.</param>
    partial void OnStageChanged(ImportStage value)
    {
        StatusMessage = null;
        LastErrorMessage = null;
        RefreshCommands();
    }

    // ── What the view shows ───────────────────────────────────────────────────

    /// <summary>The heading above the problem list (<see cref="ImportMessages.ProblemsHeading"/>).</summary>
    public string ProblemsHeading => ImportMessages.ProblemsHeading;

    /// <summary>
    /// The always-visible target line, "Sections will be added to Fall 2026 (2026-2027).", from the shown
    /// semester. When no semester, or several, are shown it says what to do instead (the same sentence as
    /// the menu item's tooltip).
    /// </summary>
    public string TargetLine
    {
        get
        {
            var target = _host.Target;
            return target is null
                ? ImportAvailability.WhyUnavailable(true, _host.ShownSemesterCount, false) ?? string.Empty
                : ImportMessages.TargetLine(target.TermName, target.AcademicYearName);
        }
    }

    /// <summary>A line above the result: the "something changed" heading (D4) or the "semester changed" note; null when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    /// <summary>True when <see cref="Notice"/> has text.</summary>
    public bool HasNotice => Notice is not null;

    /// <summary>A short confirmation ("Template saved.", "Copied."); null when none. Cleared when the stage changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    /// <summary>True when <see cref="StatusMessage"/> has text.</summary>
    public bool HasStatusMessage => StatusMessage is not null;

    /// <summary>The problems as display lines ("Row 12, Room: …"); one line when the file itself is the problem.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _problemLines = Array.Empty<string>();

    /// <summary>The summary sentence ("Will add 37 sections, …"); empty outside Ready-to-import.</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>"Already in Fall 2026, not changed: 12 sections."; null when no section is skipped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkippedText))]
    private string? _skippedText;

    /// <summary>True when <see cref="SkippedText"/> has text.</summary>
    public bool HasSkippedText => SkippedText is not null;

    /// <summary>The label of every new section ("CHEM101 A"), in file order.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _newSections = Array.Empty<string>();

    /// <summary>The new courses ("CHEM199 — Intro Chemistry").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewCourses))]
    private IReadOnlyList<string> _newCourses = Array.Empty<string>();

    /// <summary>True when at least one course will be created.</summary>
    public bool HasNewCourses => NewCourses.Count > 0;

    /// <summary>The new instructors ("Smith, Jon (JS2)").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewInstructors))]
    private IReadOnlyList<string> _newInstructors = Array.Empty<string>();

    /// <summary>True when at least one instructor will be created.</summary>
    public bool HasNewInstructors => NewInstructors.Count > 0;

    /// <summary>
    /// "New in TermPoint": the subjects, section types, meeting types, tags, campuses and rooms that will be
    /// created, grouped by kind, empty kinds left out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewInTermPoint))]
    private IReadOnlyList<ImportListGroup> _newInTermPoint = Array.Empty<ImportListGroup>();

    /// <summary>True when any setup value will be created (the list is always shown then, because a typo creates a record).</summary>
    public bool HasNewInTermPoint => NewInTermPoint.Count > 0;

    /// <summary>The warnings, in report order. They do not block the import.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    /// <summary>True when there is at least one warning.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>The message of the Nothing-to-add stage.</summary>
    [ObservableProperty]
    private string _nothingToAddText = string.Empty;

    /// <summary>"Added 37 sections, 5 courses and 8 instructors to Fall 2026 (2026-2027)."; empty before Done.</summary>
    [ObservableProperty]
    private string _doneMessage = string.Empty;

    /// <summary>
    /// The further lines of the done message, each shown on its own: the setup values and the new-rooms
    /// sentence, the "not shown" sentence (D6), and the undo hint (D5), in that order, only those that apply.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDoneNotes))]
    private IReadOnlyList<string> _doneNotes = Array.Empty<string>();

    /// <summary>True when <see cref="DoneNotes"/> has lines.</summary>
    public bool HasDoneNotes => DoneNotes.Count > 0;

    // ── Availability ──────────────────────────────────────────────────────────

    /// <summary>
    /// Why Import is unavailable right now (read-only, no semester, several semesters, a section being
    /// edited), or null when it is available. Updated live while the flyout is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportAvailable))]
    private string? _unavailableReason;

    /// <summary>True when <see cref="UnavailableReason"/> is null.</summary>
    public bool IsImportAvailable => UnavailableReason is null;

    /// <summary>
    /// Reads the three facts from the host and sets <see cref="UnavailableReason"/>, then re-evaluates the
    /// commands and the target line.
    /// </summary>
    private void RefreshAvailability()
    {
        UnavailableReason = ImportAvailability.WhyUnavailable(
            _host.IsWriteEnabled, _host.ShownSemesterCount, _host.IsEditingSection);
        OnPropertyChanged(nameof(TargetLine));
        RefreshCommands();
    }

    /// <summary>
    /// Called by the host when write access, the shown semesters or section editing changed. Refreshes the
    /// availability, and drops a result that was checked against a semester that is no longer the one shown.
    /// </summary>
    private void OnHostChanged()
    {
        RefreshAvailability();

        var showsResult = Stage is ImportStage.Problems or ImportStage.ReadyToImport or ImportStage.NothingToAdd;
        if (showsResult && _checkedTarget is not null && _host.Target?.SemesterId != _checkedTarget.SemesterId)
            ResetToChoose(ImportMessages.SemesterChangedNotice);
    }

    // ── Save template ─────────────────────────────────────────────────────────

    /// <summary>"Save template…": asks where, then saves the template workbook there.</summary>
    /// <returns>A task that completes when the template is saved or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private async Task SaveTemplate()
    {
        string? path;
        try { path = await _platform.PickFileToSaveAsync(ImportMessages.TemplateSaveRequest); }
        catch (Exception ex)
        {
            LogQuietly("choosing where to save the template", ex);
            LastErrorMessage = ImportMessages.TemplateNotSaved;
            return;
        }

        if (path is null) return;
        await SaveTemplateToAsync(path);
    }

    private bool CanSaveTemplate() => Stage == ImportStage.ReadyToChoose;

    /// <summary>
    /// Saves the template workbook that ships in the app to <paramref name="path"/>. The bytes are read on the
    /// calling thread and written through <see cref="NetworkFileOps.RunAsync(Action, string)"/>, so a stalled
    /// network folder cannot freeze the window. On success <see cref="StatusMessage"/> confirms it; on any
    /// failure <see cref="ViewModelBase.LastErrorMessage"/> says to try another folder (no path, no exception text).
    /// </summary>
    /// <param name="path">Where to write the <c>.xlsx</c> file.</param>
    /// <returns>A task that completes when the file is written or the attempt has failed.</returns>
    public async Task SaveTemplateToAsync(string path)
    {
        StatusMessage = null;
        LastErrorMessage = null;

        bool saved;
        try
        {
            var bytes = _platform.ReadTemplate();
            saved = await NetworkFileOps.RunAsync(() => File.WriteAllBytes(path, bytes), "section import save template");
        }
        catch (Exception ex)
        {
            LogQuietly("saving the template", ex);
            saved = false;
        }

        if (saved) StatusMessage = ImportMessages.TemplateSaved;
        else LastErrorMessage = ImportMessages.TemplateNotSaved;
    }

    // ── Instructions ──────────────────────────────────────────────────────────

    /// <summary>Backing field of <see cref="InstructionBlocks"/>; null until the instructions are first needed.</summary>
    private IReadOnlyList<InstructionBlock>? _instructionBlocks;

    /// <summary>Backing field of <see cref="InstructionsUnavailableText"/>; set only if loading the instructions failed.</summary>
    private string? _instructionsUnavailableText;

    /// <summary>
    /// The instructions as blocks (headings, paragraphs, bullets) for the view to draw. They are read from the
    /// platform and parsed on the first access and kept, so the file is read at most once however often the
    /// view asks. If reading fails the list is empty and <see cref="InstructionsUnavailableText"/> says so; it is
    /// not retried.
    /// </summary>
    public IReadOnlyList<InstructionBlock> InstructionBlocks => _instructionBlocks ??= LoadInstructions();

    /// <summary>
    /// null when the instructions loaded; otherwise the one line the view shows in place of them
    /// (<see cref="ImportMessages.InstructionsUnavailable"/>). Reading it loads the instructions if that has not
    /// happened yet, so it is right whichever property the view reads first.
    /// </summary>
    public string? InstructionsUnavailableText
    {
        get
        {
            _ = InstructionBlocks;
            return _instructionsUnavailableText;
        }
    }

    /// <summary>
    /// Reads and parses the instructions. A failure is logged without the path or the exception's text (rule 4),
    /// leaves <see cref="InstructionsUnavailableText"/> set, and gives an empty list.
    /// </summary>
    /// <returns>The blocks; empty when the instructions could not be read.</returns>
    private IReadOnlyList<InstructionBlock> LoadInstructions()
    {
        try
        {
            return ImportInstructions.Parse(_platform.ReadInstructions());
        }
        catch (Exception ex)
        {
            LogQuietly("reading the instructions", ex);
            _instructionsUnavailableText = ImportMessages.InstructionsUnavailable;
            return Array.Empty<InstructionBlock>();
        }
    }

    /// <summary>
    /// True while the instructions are shown in the flyout. The Ready-to-choose and Problems screens both bind
    /// to this one property, so opening them on one screen leaves them open on the other. It is not reset when
    /// the stage changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstructionsToggleText))]
    private bool _showInstructions;

    /// <summary>The label of the show/hide button: "Show instructions" while hidden, "Hide instructions" while shown.</summary>
    public string InstructionsToggleText =>
        ShowInstructions ? ImportMessages.HideInstructions : ImportMessages.ShowInstructions;

    /// <summary>"Show instructions" / "Hide instructions": shows or hides the instructions in the flyout.</summary>
    [RelayCommand]
    private void ToggleInstructions() => ShowInstructions = !ShowInstructions;

    // ── Save instructions ─────────────────────────────────────────────────────

    /// <summary>"Save instructions…": asks where, then saves the instructions there as a web page.</summary>
    /// <returns>A task that completes when the page is saved or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanSaveInstructions))]
    private async Task SaveInstructions()
    {
        string? path;
        try { path = await _platform.PickFileToSaveAsync(ImportMessages.InstructionsSaveRequest); }
        catch (Exception ex)
        {
            LogQuietly("choosing where to save the instructions", ex);
            LastErrorMessage = ImportMessages.InstructionsNotSaved;
            return;
        }

        if (path is null) return;
        await SaveInstructionsToAsync(path);
    }

    private bool CanSaveInstructions() => Stage == ImportStage.ReadyToChoose;

    /// <summary>
    /// Saves the instructions to <paramref name="path"/> as a web page (<see cref="ImportInstructionsHtml.Build"/>),
    /// written as UTF-8 through <see cref="NetworkFileOps.RunAsync(Action, string)"/>, so a stalled network folder
    /// cannot freeze the window. On success <see cref="StatusMessage"/> confirms it; when the write fails,
    /// <see cref="ViewModelBase.LastErrorMessage"/> says to try another folder (no path, no exception text). When
    /// the instructions themselves could not be loaded, no empty page is written and the error is
    /// <see cref="ImportMessages.InstructionsUnavailable"/> instead, since another folder would not help.
    /// </summary>
    /// <param name="path">Where to write the <c>.html</c> file.</param>
    /// <returns>A task that completes when the file is written or the attempt has failed.</returns>
    public async Task SaveInstructionsToAsync(string path)
    {
        StatusMessage = null;
        LastErrorMessage = null;

        var blocks = InstructionBlocks;
        if (blocks.Count == 0)
        {
            // The instructions could not be read: say so rather than writing an empty page.
            LastErrorMessage = ImportMessages.InstructionsUnavailable;
            return;
        }

        bool saved;
        try
        {
            var html = ImportInstructionsHtml.Build(blocks);
            saved = await NetworkFileOps.RunAsync(
                () => File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
                "section import save instructions");
        }
        catch (Exception ex)
        {
            LogQuietly("saving the instructions", ex);
            saved = false;
        }

        if (saved) StatusMessage = ImportMessages.InstructionsSaved;
        else LastErrorMessage = ImportMessages.InstructionsNotSaved;
    }

    // ── Choose file / Check again ─────────────────────────────────────────────

    /// <summary>"Choose file…": asks which file, then checks it.</summary>
    /// <returns>A task that completes when the file has been checked or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanChooseFile))]
    private async Task ChooseFile()
    {
        string? path;
        try { path = await _platform.PickFileToOpenAsync(); }
        catch (Exception ex)
        {
            LogQuietly("choosing the file", ex);
            LastErrorMessage = ImportMessages.CheckFailed;
            return;
        }

        if (path is null) return;
        await CheckFileAsync(path);
    }

    /// <summary>Choosing is allowed except while checking and while a result is waiting for Import or Cancel; it needs a semester to import into.</summary>
    private bool CanChooseFile() =>
        Stage is ImportStage.ReadyToChoose or ImportStage.Problems or ImportStage.NothingToAdd or ImportStage.Done
        && _host.Target is not null;

    /// <summary>"Check again": reads the same file again, after the user has fixed it.</summary>
    /// <returns>A task that completes when the file has been checked.</returns>
    [RelayCommand(CanExecute = nameof(CanCheckAgain))]
    private Task CheckAgain() => CheckFileAsync(_lastPath!);

    private bool CanCheckAgain() => Stage == ImportStage.Problems && _lastPath is not null && _host.Target is not null;

    /// <summary>
    /// Checks a file against the shown semester and shows the result. Nothing is changed in the database.
    /// <list type="bullet">
    /// <item>A problem with the file itself: Problems stage with that one message.</item>
    /// <item>Problems in the rows: Problems stage, one line each.</item>
    /// <item>No new sections: Nothing-to-add stage.</item>
    /// <item>Otherwise: Ready-to-import stage with the summary, the lists and the warnings.</item>
    /// </list>
    /// A failure inside TermPoint is logged and shown as <see cref="ImportMessages.CheckFailed"/>.
    /// </summary>
    /// <param name="path">The <c>.xlsx</c> or <c>.csv</c> file.</param>
    /// <returns>A task that completes when the stage has been set.</returns>
    public async Task CheckFileAsync(string path)
    {
        var target = _host.Target;
        if (target is null)
        {
            // No single semester is shown (it changed after the click). The target line says what to do.
            ResetToChoose(null);
            return;
        }

        _lastPath = path;
        ClearResult();
        Notice = null;
        Stage = ImportStage.Checking;

        ImportCheck check;
        try
        {
            check = await _service.CheckAsync(path, target.SemesterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImportSectionsViewModel.CheckFile");
            ShowProblems(new[] { new ImportProblem(null, null, ImportMessages.CheckFailed) });
            return;
        }

        // The user may have shown another semester while the file was being read.
        if (_host.Target?.SemesterId != target.SemesterId)
        {
            ResetToChoose(ImportMessages.SemesterChangedNotice);
            return;
        }

        _checkedTarget = target;
        if (check.HasFileProblem)
        {
            ShowProblems(new[] { new ImportProblem(null, null, check.FileProblem!) });
            return;
        }

        _sheet = check.Sheet;
        ShowReport(check.Report!);
    }

    // ── Showing a report ──────────────────────────────────────────────────────

    /// <summary>
    /// Shows a report as the stage it calls for: Problems, Nothing-to-add or Ready-to-import. Used for the first
    /// check and for the up-to-date result after the database changed (decision D4).
    /// </summary>
    /// <param name="report">The report to show; it becomes the one Import compares against.</param>
    private void ShowReport(ImportReport report)
    {
        ClearDisplay();
        _shownReport = report;
        var target = _checkedTarget!;

        if (report.HasProblems)
        {
            ShowProblems(report.Problems);
            return;
        }

        Warnings = report.Warnings.Select(w => w.Message).ToList();

        if (report.Plan.NewSections.Count == 0)
        {
            // A file with sections that are all already there says so; a file with no sections at all must not
            // claim that "every section is already in" the semester.
            NothingToAddText = report.SkippedSections.Count > 0
                ? ImportMessages.NothingToAdd(target.TermName, target.AcademicYearName)
                : ImportMessages.NoSectionsInFile;
            Stage = ImportStage.NothingToAdd;
            return;
        }

        var plan = report.Plan;
        SummaryText = ImportMessages.Summary(plan.NewSections.Count, plan.NewCourses.Count, plan.NewInstructors.Count);
        SkippedText = report.SkippedSections.Count > 0
            ? ImportMessages.Skipped(target.TermName, report.SkippedSections.Count)
            : null;
        NewSections = plan.NewSections.Select(s => s.Key.Label).ToList();
        NewCourses = plan.NewCourses.Select(c => ImportMessages.CourseLine(c.Record)).ToList();
        NewInstructors = plan.NewInstructors.Select(i => ImportMessages.InstructorLine(i.Record)).ToList();
        NewInTermPoint = ImportMessages.NewInTermPoint(plan);
        Stage = ImportStage.ReadyToImport;
    }

    /// <summary>Shows the Problems stage with these problems.</summary>
    /// <param name="problems">The problems, in the order to show.</param>
    private void ShowProblems(IReadOnlyList<ImportProblem> problems)
    {
        ClearDisplay();
        _problems = problems;
        ProblemLines = problems.Select(ImportProblemText.Line).ToList();
        Stage = ImportStage.Problems;
    }

    /// <summary>Forgets the result on screen (the report, the sheet and every list) but keeps the last path.</summary>
    private void ClearResult()
    {
        _sheet = null;
        _shownReport = null;
        _checkedTarget = null;
        ClearDisplay();
    }

    /// <summary>
    /// Empties every list and message the stages show, so nothing from an earlier result lingers when the next
    /// one (or a different stage) is shown. Keeps the sheet, the report and the checked semester.
    /// </summary>
    private void ClearDisplay()
    {
        _problems = Array.Empty<ImportProblem>();
        ProblemLines = Array.Empty<string>();
        SummaryText = string.Empty;
        SkippedText = null;
        NewSections = Array.Empty<string>();
        NewCourses = Array.Empty<string>();
        NewInstructors = Array.Empty<string>();
        NewInTermPoint = Array.Empty<ImportListGroup>();
        Warnings = Array.Empty<string>();
        NothingToAddText = string.Empty;
        DoneMessage = string.Empty;
        DoneNotes = Array.Empty<string>();
    }

    /// <summary>Clears the result and goes back to the first stage.</summary>
    /// <param name="notice">A line to show there (why the result was dropped); null for none.</param>
    private void ResetToChoose(string? notice)
    {
        ClearResult();
        Notice = notice;
        Stage = ImportStage.ReadyToChoose;
    }

    // ── Cancel / Copy list ────────────────────────────────────────────────────

    /// <summary>"Cancel": drops the checked file and goes back to the start. Nothing has been written.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => ResetToChoose(null);

    private bool CanCancel() => Stage == ImportStage.ReadyToImport;

    /// <summary>
    /// "Copy list": puts the problems on the clipboard as tab-separated text (<c>Row</c>, <c>Column</c>,
    /// <c>Problem</c>, with a header line), so pasting into Excel gives three columns.
    /// </summary>
    /// <returns>A task that completes when the text is on the clipboard, or the attempt failed.</returns>
    [RelayCommand(CanExecute = nameof(CanCopyList))]
    private async Task CopyList()
    {
        StatusMessage = null;
        LastErrorMessage = null;

        bool copied;
        try { copied = await _platform.SetClipboardTextAsync(ImportProblemText.CopyText(_problems)); }
        catch (Exception ex)
        {
            LogQuietly("copying the problem list", ex);
            copied = false;
        }

        if (copied) StatusMessage = ImportMessages.ListCopied;
        else LastErrorMessage = ImportMessages.ListNotCopied;
    }

    private bool CanCopyList() => Stage == ImportStage.Problems;

    // ── Import ────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Import": writes the checked file. Does nothing when importing is unavailable right now (it re-checks
    /// the three conditions itself rather than trusting the button), or when the shown semester is no longer
    /// the one the file was checked against.
    /// <list type="bullet">
    /// <item><b>Written:</b> Done stage with the done message, then the section views are reloaded.</item>
    /// <item><b>Changed</b> (the database changed since the check, decision D4): nothing is written; the new
    /// result is shown with <see cref="ImportMessages.ChangedNotice"/>.</item>
    /// <item><b>Failed:</b> logged; the inline message follows <see cref="WriteFailureText"/>; the stage stays
    /// Ready-to-import and nothing was written (the writer rolled back).</item>
    /// </list>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        if (Stage != ImportStage.ReadyToImport || _sheet is null || _shownReport is null || _checkedTarget is null)
            return;

        // Re-evaluate live: any of the three can have changed since the button was last updated.
        var reason = ImportAvailability.WhyUnavailable(
            _host.IsWriteEnabled, _host.ShownSemesterCount, _host.IsEditingSection);
        if (reason is not null)
        {
            UnavailableReason = reason;
            RefreshCommands();
            return;
        }

        if (_host.Target?.SemesterId != _checkedTarget.SemesterId)
        {
            ResetToChoose(ImportMessages.SemesterChangedNotice);
            return;
        }

        LastErrorMessage = null;
        var target = _checkedTarget;
        ImportOutcome outcome;
        try
        {
            outcome = _service.Import(_sheet, _shownReport, target.SemesterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImportSectionsViewModel.Import");
            LastErrorMessage = WriteFailureText.For(ex, ImportMessages.ImportFailed);
            return;
        }

        if (!outcome.WasWritten)
        {
            // Something in TermPoint changed since the check: show what the file would do now.
            ShowReport(outcome.Report);
            Notice = ImportMessages.ChangedNotice;
            return;
        }

        if (outcome.Counts.Sections == 0)
        {
            // Cannot happen after a Ready-to-import result, but if it does, say what is true.
            ShowReport(outcome.Report);
            return;
        }

        ShowDone(outcome, target);
    }

    private bool CanImport() => Stage == ImportStage.ReadyToImport && IsImportAvailable;

    /// <summary>
    /// Builds the done message and shows the Done stage, then reloads the section views. The undo hint comes
    /// from the report planned by Import itself (whether the semester was empty just before the write), not
    /// from the first check. The "not shown" sentence reads the courses after the write.
    /// </summary>
    /// <param name="outcome">What Import returned.</param>
    /// <param name="target">The semester that was written to.</param>
    private void ShowDone(ImportOutcome outcome, ImportTarget target)
    {
        var notes = new List<string>();
        var setup = ImportMessages.SetupLine(outcome.Counts);
        if (setup is not null) notes.Add(setup);

        var createdCourseIds = outcome.Report.Plan.NewSections.Select(s => s.Course.Record.Id).ToList();
        var outside = OutsideChosenSubjectsNote.Build(_focus, _courses, createdCourseIds, _logger);
        if (outside is not null) notes.Add(outside);

        if (outcome.Report.SemesterWasEmpty) notes.Add(ImportMessages.UndoHint);

        ClearResult();
        DoneMessage = ImportMessages.Done(outcome.Counts, target.TermName, target.AcademicYearName);
        DoneNotes = notes;
        Notice = null;
        Stage = ImportStage.Done;

        // After the write has committed: a failure to refresh the views must not read as a failed import.
        try { _host.ReloadSections(); }
        catch (Exception ex) { _logger.LogError(ex, "ImportSectionsViewModel.ReloadSections"); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Re-evaluates every command's enabled state.</summary>
    private void RefreshCommands()
    {
        SaveTemplateCommand.NotifyCanExecuteChanged();
        SaveInstructionsCommand.NotifyCanExecuteChanged();
        ChooseFileCommand.NotifyCanExecuteChanged();
        CheckAgainCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CopyListCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Logs a failure without the path or the exception's text (both can name the user's folders): only what
    /// was being done, the exception type and its error code.
    /// </summary>
    /// <param name="what">What was being done.</param>
    /// <param name="ex">The failure.</param>
    private void LogQuietly(string what, Exception ex) =>
        _logger.LogInfo($"ImportSectionsViewModel: {what} failed: {ex.GetType().Name} (HResult 0x{ex.HResult:X8})");
}
#endif
