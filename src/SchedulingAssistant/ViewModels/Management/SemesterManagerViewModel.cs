using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Portable ViewModel for managing the semesters within a single academic year.
/// Scoped to one year by <see cref="AcademicYearId"/>; can be embedded in any view
/// or wizard panel. Instantiated manually by the parent ViewModel — not DI-registered.
/// </summary>
public partial class SemesterManagerViewModel : ViewModelBase, IDisposable
{
    private readonly string _academicYearId;
    private readonly IAcademicYearRepository _ayRepo;
    private readonly ISemesterRepository _semRepo;
    private readonly ISectionRepository _sectionRepo;
    private readonly SemesterContext _semesterContext;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;
    private bool _disposed;

    /// <summary>True when this instance holds the write lock; gates all write-capable buttons.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    private bool CanWrite() => _lockService.IsWriter;

    /// <summary>Semesters belonging to the academic year, ordered by SortOrder.</summary>
    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();

    /// <summary>Currently selected semester in the list.</summary>
    [ObservableProperty] private Semester? _selectedSemester;

    /// <summary>Non-null when the inline Add or Rename form is open.</summary>
    [ObservableProperty] private SemesterEditViewModel? _editVm;

    /// <param name="academicYearId">The academic year whose semesters are managed.</param>
    /// <param name="ayRepo">Academic year repository; required by SemesterContext.Reload.</param>
    /// <param name="semRepo">Semester repository for CRUD operations.</param>
    /// <param name="sectionRepo">Section repository; used to guard deletion when sections exist.</param>
    /// <param name="semesterContext">Singleton context; reloaded after every mutation so top-bar dropdowns stay current.</param>
    /// <param name="dialog">Service for confirmation and error dialogs.</param>
    /// <param name="lockService">Write lock; gates all edit operations when another user holds the lock.</param>
    public SemesterManagerViewModel(
        string academicYearId,
        IAcademicYearRepository ayRepo,
        ISemesterRepository semRepo,
        ISectionRepository sectionRepo,
        SemesterContext semesterContext,
        IDialogService dialog,
        WriteLockService lockService)
    {
        _academicYearId   = academicYearId;
        _ayRepo           = ayRepo;
        _semRepo          = semRepo;
        _sectionRepo      = sectionRepo;
        _semesterContext  = semesterContext;
        _dialog           = dialog;
        _lockService      = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    /// <summary>Reloads the semester list from the database for this academic year.</summary>
    private void Load() =>
        Semesters = new ObservableCollection<Semester>(_semRepo.GetByAcademicYear(_academicYearId));

    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSemesterChanged(Semester? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    private bool CanMoveUp()   => _lockService.IsWriter && SelectedSemester != null && Semesters.IndexOf(SelectedSemester) > 0;
    private bool CanMoveDown() => _lockService.IsWriter && SelectedSemester != null && Semesters.IndexOf(SelectedSemester) < Semesters.Count - 1;

    /// <summary>Moves the selected semester one position earlier and persists the new sort order.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => ApplyMove(Semesters.IndexOf(SelectedSemester!), -1);

    /// <summary>Moves the selected semester one position later and persists the new sort order.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => ApplyMove(Semesters.IndexOf(SelectedSemester!), +1);

    /// <summary>
    /// Reorders the semester at <paramref name="index"/> by <paramref name="delta"/> positions
    /// (+1 or -1), re-packs SortOrder values densely, and persists every changed record.
    /// </summary>
    private void ApplyMove(int index, int delta)
    {
        var list = Semesters.ToList();
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(index + delta, item);

        for (var i = 0; i < list.Count; i++)
            list[i].SortOrder = i;

        foreach (var s in list)
            _semRepo.Update(s);

        var selectedId = item.Id;
        _semesterContext.Reload(_ayRepo, _semRepo);
        Load();
        SelectedSemester = Semesters.FirstOrDefault(s => s.Id == selectedId);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    /// <summary>Opens the inline Add form for a new semester at the bottom of the list.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var semester = new Semester
        {
            AcademicYearId = _academicYearId,
            SortOrder      = Semesters.Count > 0 ? Semesters.Max(s => s.SortOrder) + 1 : 0
        };
        EditVm = new SemesterEditViewModel(semester, isNew: true,
            onSave: s =>
            {
                try
                {
                    _semRepo.Insert(s);
                    _semesterContext.Reload(_ayRepo, _semRepo);
                    Load();
                    EditVm = null;
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, "SemesterManagerViewModel.Add");
                    _ = _dialog.ShowError("The save could not be completed. Please try again.");
                }
            },
            onCancel: () => EditVm = null);
    }

    /// <summary>Opens the inline Rename form for the currently selected semester.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Rename()
    {
        if (SelectedSemester is null) return;
        // Work on a copy so Cancel leaves the original untouched.
        var copy = new Semester
        {
            Id             = SelectedSemester.Id,
            AcademicYearId = SelectedSemester.AcademicYearId,
            Name           = SelectedSemester.Name,
            SortOrder      = SelectedSemester.SortOrder
        };
        EditVm = new SemesterEditViewModel(copy, isNew: false,
            onSave: s =>
            {
                try
                {
                    _semRepo.Update(s);
                    _semesterContext.Reload(_ayRepo, _semRepo);
                    Load();
                    EditVm = null;
                }
                catch (Exception ex)
                {
                    App.Logger.LogError(ex, "SemesterManagerViewModel.Rename");
                    _ = _dialog.ShowError("The save could not be completed. Please try again.");
                }
            },
            onCancel: () => EditVm = null);
    }

    /// <summary>
    /// Deletes the selected semester after confirming it is empty.
    /// If the semester still contains sections the user is directed to the Empty Semester tool instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedSemester is null) return;

        var count = _sectionRepo.CountBySemesterId(SelectedSemester.Id);
        if (count > 0)
        {
            await _dialog.ShowError(
                $"\"{SelectedSemester.Name}\" contains {count} section{(count == 1 ? "" : "s")}. " +
                "Use the Empty Semester tool to remove all sections before deleting a semester.");
            return;
        }

        if (!await _dialog.Confirm($"Delete semester \"{SelectedSemester.Name}\"?"))
            return;

        try
        {
            _semRepo.Delete(SelectedSemester.Id);
            _semesterContext.Reload(_ayRepo, _semRepo);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SemesterManagerViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }

    /// <summary>
    /// Unsubscribes from <see cref="WriteLockService.LockStateChanged"/> to prevent a memory
    /// leak when this instance is replaced because the selected academic year changes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lockService.LockStateChanged -= OnLockStateChanged;
    }
}
