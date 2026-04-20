using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class AcademicYearListViewModel : ViewModelBase, IDismissableEditor
{
    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly SemesterContext _semesterContext;
    private readonly ILegalStartTimeRepository _legalStartTimeRepo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    [ObservableProperty] private ObservableCollection<AcademicYear> _academicYears = new();
    [ObservableProperty] private AcademicYear? _selectedAcademicYear;
    [ObservableProperty] private AcademicYearEditViewModel? _editVm;

    /// <inheritdoc/>
    public bool DismissActiveEditor()
    {
        if (EditVm is not null) { EditVm.CancelCommand.Execute(null); return true; }
        if (SemesterManager?.EditVm is not null) { SemesterManager.EditVm.CancelCommand.Execute(null); return true; }
        return false;
    }

    /// <summary>
    /// The semester manager panel scoped to the currently selected academic year.
    /// Rebuilt whenever <see cref="SelectedAcademicYear"/> changes.
    /// </summary>
    [ObservableProperty] private SemesterManagerViewModel? _semesterManager;

    /// <summary>
    /// Set by the view. Called when adding a new academic year to ask if the user
    /// wants to copy the start-time/block-length setup from the previous year.
    /// Returns the source AY ID to copy from, or null to skip.
    /// </summary>
    public Func<string, string, Task<string?>>? ConfirmCopyStartTimes { get; set; }

    /// <summary>
    /// Set by the view. Called when adding a new academic year (not the first) to ask whether
    /// semester names should be copied from the previous year. Returns true to copy.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmCopySemesters { get; set; }

    public AcademicYearListViewModel(
        IAcademicYearRepository ayRepo,
        ISemesterRepository semRepo,
        ISectionRepository sectionRepo,
        SemesterContext semesterContext,
        ILegalStartTimeRepository legalStartTimeRepo,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _ayRepo = ayRepo;
        _semRepo = semRepo;
        _sectionRepo = sectionRepo;
        _semesterContext = semesterContext;
        _legalStartTimeRepo = legalStartTimeRepo;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load()
    {
        AcademicYears = new ObservableCollection<AcademicYear>(_ayRepo.GetAll());
        SelectedAcademicYear = AcademicYears.FirstOrDefault();
    }

    /// <summary>
    /// Rebuilds <see cref="SemesterManager"/> whenever the selected academic year changes,
    /// disposing the previous instance to release its WriteLockService subscription.
    /// </summary>
    partial void OnSelectedAcademicYearChanged(AcademicYear? value)
    {
        SemesterManager?.Dispose();
        SemesterManager = value is null ? null
            : new SemesterManagerViewModel(
                value.Id, _ayRepo, _semRepo, _sectionRepo,
                _semesterContext, _dialog, _lockService);
    }

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CopySemester()
    {
        var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
        mainVm.NavigateToCopySemesterCommand.Execute(null);
    }

    [RelayCommand]
    private void EmptySemester()
    {
        var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
        mainVm.NavigateToEmptySemesterCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var ay = new AcademicYear();
        EditVm = new AcademicYearEditViewModel(ay,
            onSave: null,
            onCancel: () => EditVm = null,
            nameExists: name => _ayRepo.ExistsByName(name),
            onSaveAsync: async saved =>
            {
                try
                {
                    _ayRepo.Insert(saved);

                    // Find the new year's position in the sorted list to identify the
                    // adjacent predecessor (if any) to offer copying semesters/start times from.
                    var allAYs = _ayRepo.GetAll().OrderBy(a => a.Name).ToList();
                    var savedIndex = allAYs.FindIndex(a => a.Id == saved.Id);

                    if (savedIndex > 0)
                    {
                        var prevAY = allAYs[savedIndex - 1];

                        // Ask whether to copy semester names from the previous year.
                        bool copySemesters = ConfirmCopySemesters is not null
                            && await ConfirmCopySemesters(prevAY.Name);

                        if (copySemesters)
                        {
                            var prevSemesters = _semRepo.GetByAcademicYear(prevAY.Id);
                            for (int i = 0; i < prevSemesters.Count; i++)
                            {
                                _semRepo.Insert(new Semester
                                {
                                    AcademicYearId = saved.Id,
                                    Name           = prevSemesters[i].Name,
                                    Color          = prevSemesters[i].Color,
                                    SortOrder      = prevSemesters[i].SortOrder
                                });
                            }
                        }
                        // else: new year starts with no semesters; user adds them via the panel.

                        // Ask whether to copy block length / start time setup.
                        string? fromAyId = null;
                        if (ConfirmCopyStartTimes is not null)
                            fromAyId = await ConfirmCopyStartTimes(prevAY.Name, prevAY.Id);

                        if (fromAyId is not null)
                            _legalStartTimeRepo.CopyFromPreviousYear(saved.Id, fromAyId);
                        // else: user declines — new year has no start times configured yet.
                    }

                    _semesterContext.Reload(_ayRepo, _semRepo);
                    Load();
                    EditVm = null;
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, "AcademicYearListViewModel.Add");
                    await _dialog.ShowError("The save could not be completed. Please try again.");
                }
            });
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedAcademicYear is null) return;

        var sectionCount = _sectionRepo.CountByAcademicYear(SelectedAcademicYear.Id);
        var bodyText = sectionCount > 0
            ? $"Delete academic year \"{SelectedAcademicYear.Name}\"?\n\nWarning: this academic year has {sectionCount} section{(sectionCount == 1 ? "" : "s")} across its semesters. They will all be permanently deleted."
            : $"Delete academic year \"{SelectedAcademicYear.Name}\" and all its semesters?";

        if (!await _dialog.Confirm(bodyText)) return;

        try
        {
            _semRepo.DeleteByAcademicYear(SelectedAcademicYear.Id);
            _ayRepo.Delete(SelectedAcademicYear.Id);
            _semesterContext.Reload(_ayRepo, _semRepo);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AcademicYearListViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }

}
