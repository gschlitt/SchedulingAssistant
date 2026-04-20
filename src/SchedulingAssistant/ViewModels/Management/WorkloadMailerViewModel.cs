using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace SchedulingAssistant.ViewModels.Management;

// ── Step items ───────────────────────────────────────────────────────────────

/// <summary>
/// Represents the current UI step of the Workload Mailer flyout.
/// </summary>
public enum MailerStep { Setup, Sending, Done }

/// <summary>
/// A semester checkbox item local to the Workload Mailer, independent of the
/// global SemesterContext. Notifies the parent ViewModel when its selection changes.
/// </summary>
public partial class MailerSemesterCheckItem : ObservableObject
{
    /// <summary>The semester this item represents.</summary>
    public Semester Semester { get; init; } = null!;

    /// <summary>Display name shown in the checkbox list.</summary>
    public string DisplayName => Semester.Name;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Fired by the parent ViewModel to react to selection changes.</summary>
    internal event Action<MailerSemesterCheckItem>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);
}

/// <summary>
/// Represents an instructor row in the Workload Mailer selection list.
/// Instructors without an email address cannot be selected.
/// </summary>
public partial class MailerInstructorItem : ObservableObject
{
    /// <summary>The underlying instructor entity.</summary>
    public Instructor Instructor { get; init; } = null!;

    /// <summary>True if the instructor has a non-empty email address.</summary>
    public bool HasEmail => !string.IsNullOrWhiteSpace(Instructor.Email);

    /// <summary>Full name for display.</summary>
    public string DisplayName => $"{Instructor.FirstName} {Instructor.LastName}".Trim();

    /// <summary>Shows the email address, or "(no email)" as a placeholder.</summary>
    public string EmailDisplay => HasEmail ? Instructor.Email : "(no email)";

    [ObservableProperty]
    private bool _isSelected;
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

/// <summary>
/// Drives the Workload Mailer flyout, which lets administrators email each
/// instructor their workload summary for one or more semesters.
///
/// The flow has two steps:
///   1. Setup — choose AY, semesters, instructors, and the email template.
///   2. Sending — work through selected instructors one at a time, preview the
///      rendered email, and open it in the default mail client via mailto:.
/// </summary>
public partial class WorkloadMailerViewModel : ViewModelBase
{
    /// <summary>Category label shown in the Export flyout sidebar.</summary>
    public string DisplayName => "Workload Mailer";

    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly IInstructorRepository _instructorRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly IReleaseRepository _releaseRepo;
    private readonly ICourseRepository _courseRepo;

    // Resolved when BeginSending is called; used throughout the Sending step.
    private List<MailerInstructorItem> _queue = new();
    private Dictionary<string, Course> _courseMap = new();
    private List<MailerSemesterCheckItem> _selectedSemesters = new();
    private string _ayName = string.Empty;
    private int _currentIndex;

    // ── Step state ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    [NotifyPropertyChangedFor(nameof(IsSending))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    private MailerStep _step = MailerStep.Setup;

    /// <summary>True while the Setup panel should be shown.</summary>
    public bool IsSetup => Step == MailerStep.Setup;

    /// <summary>True while the Sending panel should be shown.</summary>
    public bool IsSending => Step == MailerStep.Sending;

    /// <summary>True when all instructors have been processed.</summary>
    public bool IsDone => Step == MailerStep.Done;

    // ── Setup state ───────────────────────────────────────────────────────────

    /// <summary>All academic years available for selection.</summary>
    [ObservableProperty]
    private ObservableCollection<AcademicYear> _academicYears = new();

    /// <summary>The academic year chosen in the Setup dropdown.</summary>
    [ObservableProperty]
    private AcademicYear? _selectedAcademicYear;

    /// <summary>Semester checkboxes for the selected academic year.</summary>
    [ObservableProperty]
    private ObservableCollection<MailerSemesterCheckItem> _semesterItems = new();

    /// <summary>All active instructors, shown as selectable checkboxes.</summary>
    [ObservableProperty]
    private ObservableCollection<MailerInstructorItem> _instructorItems = new();

    /// <summary>Email subject template. May contain {FirstName}, {LastName}, {AcademicYear}, {Semester}.</summary>
    [ObservableProperty]
    private string _subjectTemplate = string.Empty;

    /// <summary>
    /// Email body template. May contain {FirstName}, {LastName}, {AcademicYear},
    /// {Semester}, and {Workload}.
    /// </summary>
    [ObservableProperty]
    private string _bodyTemplate = string.Empty;

    // ── Sending state ─────────────────────────────────────────────────────────

    /// <summary>Human-readable progress label, e.g. "Instructor 2 of 8".</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>Display name of the instructor currently being emailed.</summary>
    [ObservableProperty]
    private string _currentInstructorName = string.Empty;

    /// <summary>Email address of the instructor currently being emailed.</summary>
    [ObservableProperty]
    private string _currentEmail = string.Empty;

    /// <summary>Rendered subject line for the current instructor (read-only).</summary>
    [ObservableProperty]
    private string _renderedSubject = string.Empty;

    /// <summary>Rendered body for the current instructor. Editable before opening mail.</summary>
    [ObservableProperty]
    private string _renderedBody = string.Empty;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the ViewModel, loading academic years and instructors from the
    /// database, and restoring the saved email template from AppSettings.
    /// </summary>
    /// <param name="ayRepo">Repository for academic years.</param>
    /// <param name="semRepo">Repository for semesters.</param>
    /// <param name="instructorRepo">Repository for instructors.</param>
    /// <param name="sectionRepo">Repository for sections (used during sending).</param>
    /// <param name="releaseRepo">Repository for non-instructional releases (used during sending).</param>
    /// <param name="courseRepo">Repository for courses (used to resolve course codes during sending).</param>
    public WorkloadMailerViewModel(
        IAcademicYearRepository ayRepo,
        ISemesterRepository semRepo,
        IInstructorRepository instructorRepo,
        ISectionRepository sectionRepo,
        IReleaseRepository releaseRepo,
        ICourseRepository courseRepo)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _instructorRepo = instructorRepo;
        _sectionRepo = sectionRepo;
        _releaseRepo = releaseRepo;
        _courseRepo = courseRepo;

        LoadSetupData();
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads academic years and instructors from the database, and restores the
    /// email template from AppSettings.
    /// </summary>
    private void LoadSetupData()
    {
        var settings = AppSettings.Current;
        SubjectTemplate = settings.WorkloadMailerSubject;
        BodyTemplate = settings.WorkloadMailerBody;

        // Load academic years
        var ays = _ayRepo.GetAll();
        AcademicYears = new ObservableCollection<AcademicYear>(ays);
        SelectedAcademicYear = ays.FirstOrDefault();

        // Load instructors (all, including inactive — admin may want to email an outgoing instructor)
        var instructors = _instructorRepo.GetAll();
        InstructorItems = new ObservableCollection<MailerInstructorItem>(
            instructors.Select(i => new MailerInstructorItem
            {
                Instructor = i,
                IsSelected = i.IsActive && !string.IsNullOrWhiteSpace(i.Email)
            }));
    }

    /// <summary>
    /// Called when the selected academic year changes. Rebuilds the semester
    /// checkbox list for the newly selected year.
    /// </summary>
    partial void OnSelectedAcademicYearChanged(AcademicYear? value)
    {
        SemesterItems.Clear();
        if (value is null) return;

        var semesters = _semRepo.GetByAcademicYear(value.Id);
        foreach (var sem in semesters)
        {
            var item = new MailerSemesterCheckItem { Semester = sem, IsSelected = true };
            item.SelectionChanged += _ => { /* could trigger CanExecute re-eval if needed */ };
            SemesterItems.Add(item);
        }
    }

    /// <summary>
    /// Saves the current subject and body templates to AppSettings for reuse in
    /// future sessions, without starting the send process.
    /// </summary>
    [RelayCommand]
    private void SaveTemplate()
    {
        var settings = AppSettings.Current;
        settings.WorkloadMailerSubject = SubjectTemplate;
        settings.WorkloadMailerBody = BodyTemplate;
        settings.Save();
    }

    /// <summary>
    /// Selects all instructors that have an email address.
    /// </summary>
    [RelayCommand]
    private void SelectAllInstructors()
    {
        foreach (var item in InstructorItems.Where(i => i.HasEmail))
            item.IsSelected = true;
    }

    /// <summary>
    /// Deselects all instructors.
    /// </summary>
    [RelayCommand]
    private void DeselectAllInstructors()
    {
        foreach (var item in InstructorItems)
            item.IsSelected = false;
    }

    /// <summary>
    /// Validates the setup selections and transitions to the Sending step.
    /// Saves the current email template to AppSettings for reuse in future sessions.
    /// </summary>
    [RelayCommand]
    private void BeginSending()
    {
        _selectedSemesters = SemesterItems.Where(s => s.IsSelected).ToList();
        _queue = InstructorItems.Where(i => i.IsSelected && i.HasEmail).ToList();

        if (_selectedSemesters.Count == 0 || _queue.Count == 0)
            return;

        // Persist template for reuse
        var settings = AppSettings.Current;
        settings.WorkloadMailerSubject = SubjectTemplate;
        settings.WorkloadMailerBody = BodyTemplate;
        settings.Save();

        // Cache AY name and course lookup
        _ayName = SelectedAcademicYear?.Name ?? string.Empty;
        var allCourses = _courseRepo.GetAll();
        _courseMap = allCourses.ToDictionary(c => c.Id, c => c);

        // Begin
        _currentIndex = 0;
        LoadAndRenderCurrent();
        Step = MailerStep.Sending;
    }

    // ── Sending ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the current instructor's rendered email in the system default mail client
    /// via a mailto: URI, then advances to the next instructor.
    /// </summary>
    [RelayCommand]
    private void OpenInMail()
    {
        var subject = Uri.EscapeDataString(RenderedSubject);
        var body = Uri.EscapeDataString(RenderedBody);
        var mailto = $"mailto:{Uri.EscapeDataString(CurrentEmail)}?subject={subject}&body={body}";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = mailto, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Logger.LogWarning($"WorkloadMailer: failed to open mailto URI — {ex.Message}");
        }

        Advance();
    }

    /// <summary>
    /// Skips the current instructor without opening a mail window, and advances
    /// to the next one.
    /// </summary>
    [RelayCommand]
    private void Skip() => Advance();

    /// <summary>
    /// Returns to the Setup step so the user can adjust selections or template text.
    /// </summary>
    [RelayCommand]
    private void BackToSetup() => Step = MailerStep.Setup;

    /// <summary>
    /// Advances to the next instructor in the queue. Transitions to Done when the
    /// queue is exhausted.
    /// </summary>
    private void Advance()
    {
        _currentIndex++;
        if (_currentIndex >= _queue.Count)
        {
            Step = MailerStep.Done;
            return;
        }
        LoadAndRenderCurrent();
    }

    /// <summary>
    /// Loads workload data for the current instructor and renders the subject and
    /// body templates with the appropriate substitution values.
    /// </summary>
    private void LoadAndRenderCurrent()
    {
        var item = _queue[_currentIndex];
        var instructor = item.Instructor;

        ProgressText = $"Instructor {_currentIndex + 1} of {_queue.Count}";
        CurrentInstructorName = item.DisplayName;
        CurrentEmail = instructor.Email;

        var semesterNames = string.Join(", ", _selectedSemesters.Select(s => s.Semester.Name));
        var workloadBlock = BuildWorkloadBlock(instructor);

        RenderedSubject = RenderTemplate(SubjectTemplate, instructor, _ayName, semesterNames, workloadBlock);
        RenderedBody = RenderTemplate(BodyTemplate, instructor, _ayName, semesterNames, workloadBlock);
    }

    // ── Workload block builder ─────────────────────────────────────────────────

    /// <summary>
    /// Builds the plain-text workload block for one instructor across all selected
    /// semesters. Each semester is shown as a labelled section with assigned sections
    /// (including schedule and workload value) followed by any non-instructional releases.
    /// </summary>
    /// <param name="instructor">The instructor whose workload to summarise.</param>
    /// <returns>A formatted plain-text string ready for insertion via {Workload}.</returns>
    private string BuildWorkloadBlock(Instructor instructor)
    {
        var sb = new StringBuilder();

        foreach (var semItem in _selectedSemesters)
        {
            var semester = semItem.Semester;

            // Header: semester name + underline
            sb.AppendLine(semester.Name);
            sb.AppendLine(new string('─', Math.Max(semester.Name.Length, 9)));

            var sections = _sectionRepo.GetAll(semester.Id)
                .Where(s => s.InstructorAssignments.Any(a => a.InstructorId == instructor.Id))
                .ToList();

            var releases = _releaseRepo.GetByInstructor(semester.Id, instructor.Id);

            if (sections.Count == 0 && releases.Count == 0)
            {
                sb.AppendLine("  (No assignments)");
                sb.AppendLine();
                continue;
            }

            decimal semesterTotal = 0m;

            // Instructional sections
            foreach (var section in sections)
            {
                var assignment = section.InstructorAssignments
                    .First(a => a.InstructorId == instructor.Id);
                var workload = assignment.Workload ?? 1m;
                semesterTotal += workload;

                var courseCode = section.CourseId is not null && _courseMap.TryGetValue(section.CourseId, out var course)
                    ? course.CalendarCode
                    : "???";

                var scheduleText = FormatSchedule(section.Schedule);
                var label = string.IsNullOrEmpty(scheduleText)
                    ? $"  {courseCode} {section.SectionCode}"
                    : $"  {courseCode} {section.SectionCode}  —  {scheduleText}";

                sb.AppendLine($"{label}  [{workload:G}]");
            }

            // Non-instructional releases
            if (releases.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Releases:");
                foreach (var release in releases)
                {
                    semesterTotal += release.WorkloadValue;
                    sb.AppendLine($"  {release.Title}  [{release.WorkloadValue:G}]");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"  Semester Total: {semesterTotal:G}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Formats a section's schedule as a concise human-readable string.
    /// Meetings that share the same start time and duration are grouped onto a single line
    /// with their days listed together (e.g. "Mon/Wed/Fri 09:00–10:15").
    /// Multiple distinct time slots are separated by semicolons.
    /// </summary>
    /// <param name="schedule">The list of day-schedule entries for the section.</param>
    /// <returns>
    /// A formatted schedule string, or an empty string if the section has no schedule.
    /// </returns>
    private static string FormatSchedule(List<SectionDaySchedule> schedule)
    {
        if (schedule.Count == 0) return string.Empty;

        // Group meetings by (StartMinutes, DurationMinutes) so e.g. MWF 10:00 become one entry.
        var groups = schedule
            .GroupBy(m => (m.StartMinutes, m.DurationMinutes))
            .OrderBy(g => g.Key.StartMinutes);

        var parts = groups.Select(g =>
        {
            var days = string.Join("/", g.OrderBy(m => m.Day).Select(m => DayName(m.Day)));
            var start = FormatMinutes(g.Key.StartMinutes);
            var end = FormatMinutes(g.Key.StartMinutes + g.Key.DurationMinutes);
            return $"{days} {start}–{end}";
        });

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Converts a day-of-week integer (1=Monday … 7=Sunday) to its three-letter abbreviation.
    /// </summary>
    private static string DayName(int day) => day switch
    {
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        7 => "Sun",
        _ => $"Day{day}"
    };

    /// <summary>
    /// Converts a time expressed as minutes from midnight to "HH:MM" format.
    /// </summary>
    private static string FormatMinutes(int totalMinutes)
    {
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        return $"{h:D2}:{m:D2}";
    }

    // ── Template rendering ────────────────────────────────────────────────────

    /// <summary>
    /// Substitutes all known placeholders in <paramref name="template"/> with
    /// values derived from the current instructor and selected semesters.
    /// </summary>
    /// <param name="template">The template string containing placeholders.</param>
    /// <param name="instructor">The instructor whose values fill the name placeholders.</param>
    /// <param name="ayName">The academic year name for {AcademicYear}.</param>
    /// <param name="semesterNames">Comma-separated semester names for {Semester}.</param>
    /// <param name="workloadBlock">Pre-built workload block for {Workload}.</param>
    /// <returns>The template with all placeholders replaced.</returns>
    private static string RenderTemplate(
        string template,
        Instructor instructor,
        string ayName,
        string semesterNames,
        string workloadBlock)
    {
        return template
            .Replace("{FirstName}", instructor.FirstName)
            .Replace("{LastName}", instructor.LastName)
            .Replace("{AcademicYear}", ayName)
            .Replace("{Semester}", semesterNames)
            .Replace("{Workload}", workloadBlock);
    }
}
