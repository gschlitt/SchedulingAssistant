using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services;
using System.Collections.ObjectModel;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Backs the Empty Semester screen: lets the user pick a semester and delete everything attached to
/// it (sections, meetings, releases, scheduled commitments, scheduling notes and watches) so the
/// semester can then be deleted from Academic Years. All counting, the loaded-semester guard and the
/// delete itself are owned by <see cref="SemesterContentsService"/> (spec item 8); this class only
/// presents them.
/// </summary>
public partial class EmptySemesterViewModel : ViewModelBase, IDisposable
{
    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly SemesterContentsService _contentsService;
    private readonly SemesterContext _semesterContext;
    private readonly WriteLockService _lockService;

    /// <summary>True when the current user holds the write lock; gates the Empty Semester button.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _selectedAcademicYear;
    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();

    /// <summary>The semester chosen in the picker; changing it recounts its contents and re-checks the loaded guard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmpty))]
    [NotifyCanExecuteChangedFor(nameof(EmptyCommand))]
    private Semester? _selectedSemester;

    /// <summary>
    /// What the selected semester currently holds, per kind (<c>null</c> when no semester is selected).
    /// Refreshed on selection change and after an empty; drives <see cref="ContentsSummary"/> and
    /// <see cref="CanEmpty"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentsSummary))]
    [NotifyPropertyChangedFor(nameof(CanEmpty))]
    [NotifyCanExecuteChangedFor(nameof(EmptyCommand))]
    private SemesterContents? _contents;

    /// <summary>
    /// True when the selected semester is one of the semesters currently loaded in the main view
    /// (any of them in multi-semester mode, not just the first). A loaded semester cannot be emptied.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEmpty))]
    [NotifyCanExecuteChangedFor(nameof(EmptyCommand))]
    private bool _isCurrentlyLoaded;

    [ObservableProperty] private string? _statusMessage;

    /// <summary>
    /// One-line description of what the selected semester contains, bound by the view:
    /// "This semester contains 12 sections, 3 meetings.", "This semester is empty.", or <c>null</c>
    /// when no semester is selected.
    /// </summary>
    public string? ContentsSummary => Contents switch
    {
        null => null,
        { IsEmpty: true } => "This semester is empty.",
        _ => $"This semester contains {Contents.Describe()}."
    };

    /// <summary>
    /// True when Empty Semester may run: the user holds the write lock, a semester is selected, it
    /// is not loaded, and it has something to remove.
    /// </summary>
    public bool CanEmpty =>
        _lockService.IsWriter && SelectedSemester is not null && !IsCurrentlyLoaded && Contents is { IsEmpty: false };

    /// <summary>Set by the view. Called before deletion with (semesterLabel, contentsDescription): the
    /// label names the academic year and the semester (e.g. "2026-2027 Early Summer"), and the
    /// description lists every kind about to be deleted (e.g. "12 sections, 3 meetings").
    /// Should return true if the user confirms, false to cancel.</summary>
    public Func<string, string, Task<bool>>? ConfirmEmpty { get; set; }

    /// <summary>Set by the view. Called when an error occurs.</summary>
    public Func<string, Task>? ShowError { get; set; }

    /// <param name="ayRepo">Academic years, for the first picker.</param>
    /// <param name="semRepo">Semesters, for the second picker.</param>
    /// <param name="contentsService">Counts a semester's contents, knows which semesters are loaded, and does the empty.</param>
    /// <param name="semesterContext">Raises a change when the main view's loaded semesters change, so the guard stays current while this screen is open.</param>
    /// <param name="lockService">Write lock; gates the Empty Semester button.</param>
    public EmptySemesterViewModel(
        IAcademicYearRepository ayRepo,
        ISemesterRepository semRepo,
        SemesterContentsService contentsService,
        SemesterContext semesterContext,
        WriteLockService lockService)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _contentsService = contentsService;
        _semesterContext = semesterContext;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        _semesterContext.PropertyChanged += OnSemesterContextChanged;
        Load();
    }

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        SelectedAcademicYear = AcademicYears.FirstOrDefault();
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/>, <see cref="CanEmpty"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        OnPropertyChanged(nameof(CanEmpty));
        EmptyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Re-checks the loaded-semester guard when the main view's selected semesters change while this
    /// screen is open, so Empty Semester cannot stay enabled for a semester that has just been loaded.
    /// </summary>
    private void OnSemesterContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SemesterContext.SelectedSemesters)) return;

        // Only touch the status line when the guard actually flips; otherwise leave any "Done" message alone.
        var loaded = SelectedSemester is not null && _contentsService.IsLoaded(SelectedSemester.Id);
        if (loaded != IsCurrentlyLoaded)
            UpdateCurrentlyLoadedStatus();
    }

    partial void OnSelectedAcademicYearChanged(AcademicYear? value)
    {
        LoadSemesters();
    }

    partial void OnSelectedSemesterChanged(Semester? value)
    {
        UpdateContents();
        UpdateCurrentlyLoadedStatus();
    }

    private void LoadSemesters()
    {
        if (SelectedAcademicYear is null)
        {
            Semesters = new ObservableCollection<Semester>();
            SelectedSemester = null;
            return;
        }
        Semesters = new ObservableCollection<Semester>(
            _semRepo.GetByAcademicYear(SelectedAcademicYear.Id));
        SelectedSemester = Semesters.FirstOrDefault();
    }

    /// <summary>Recounts everything attached to the selected semester (<c>null</c> when none is selected).</summary>
    private void UpdateContents() =>
        Contents = SelectedSemester is null ? null : _contentsService.CountContents(SelectedSemester.Id);

    /// <summary>
    /// Recomputes <see cref="IsCurrentlyLoaded"/> for the selected semester (true for ANY of the
    /// semesters selected in the main view) and sets or clears the advisory status message.
    /// </summary>
    private void UpdateCurrentlyLoadedStatus()
    {
        if (SelectedSemester is null)
        {
            IsCurrentlyLoaded = false;
            StatusMessage = null;
            return;
        }

        IsCurrentlyLoaded = _contentsService.IsLoaded(SelectedSemester.Id);

        if (IsCurrentlyLoaded)
        {
            StatusMessage = "This semester is currently loaded. To empty it, first switch to a different semester in the main view.";
        }
        else
        {
            StatusMessage = null;
        }
    }

    /// <summary>
    /// Deletes everything attached to the selected semester after the user confirms the exact list
    /// of what will go. The service refuses a loaded semester (its message is shown); any other
    /// failure is logged and a generic message is shown.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEmpty))]
    private async Task Empty()
    {
        if (SelectedSemester is null) return;
        var semester = SelectedSemester;

        // Recount so the confirmation lists what is in the semester now, not what it held when it was selected.
        UpdateContents();
        if (Contents is not { IsEmpty: false } contents) return;

        // Confirm deletion, naming the academic year and semester (semester names such as "Fall"
        // repeat every year) and listing every kind of row that will be removed.
        var label = SelectedAcademicYear is { } year ? $"{year.Name} {semester.Name}" : semester.Name;
        if (ConfirmEmpty is null || !await ConfirmEmpty(label, contents.Describe()))
            return;

        try
        {
            _contentsService.EmptySemester(semester.Id);
            UpdateContents();
            StatusMessage = $"Done. Everything has been removed from {semester.Name}.";
        }
        catch (InvalidOperationException ex)
        {
            // The service's own refusal (the semester became loaded); its text is already user-facing.
            if (ShowError is not null)
                await ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "EmptySemesterViewModel.Empty");
            if (ShowError is not null)
                await ShowError("The operation could not be completed. Please try again.");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
        mainVm.CloseFlyoutCommand.Execute(null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lockService.LockStateChanged -= OnLockStateChanged;
        _semesterContext.PropertyChanged -= OnSemesterContextChanged;
    }
}
