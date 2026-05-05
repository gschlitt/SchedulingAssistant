using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// Display wrapper for a single <see cref="Semester"/> row in the semester manager list.
/// Exposes only <see cref="HexColor"/> (a <c>#RRGGBB</c> string); the view binds this to
/// <c>ColorView.Color</c> via <c>HexToColorConverter</c>, which keeps Avalonia.Media types
/// out of the ViewModel. When the color changes, the underlying model is updated and
/// persisted via the <paramref name="onColorChanged"/> callback.
/// </summary>
public partial class SemesterRowViewModel : ViewModelBase
{
    /// <summary>The underlying semester model; mutated when name or color changes.</summary>
    public Semester Semester { get; }

    /// <summary>Semester name for display in the list column.</summary>
    public string Name => Semester.Name;

    /// <summary>
    /// Current semester color in <c>#RRGGBB</c> hex format, bound two-way to the inline
    /// <c>ColorView</c> flyout (via <c>HexToColorConverter</c>). Changes are written back
    /// to <see cref="Semester.Color"/> and persisted via the callback.
    /// </summary>
    [ObservableProperty] private string _hexColor = string.Empty;

    private readonly Action<Semester> _onColorChanged;

    /// <param name="semester">The semester to wrap.</param>
    /// <param name="onColorChanged">Callback invoked after the color changes; responsible for persisting the update.</param>
    public SemesterRowViewModel(Semester semester, Action<Semester> onColorChanged)
    {
        Semester        = semester;
        _onColorChanged = onColorChanged;
        _hexColor       = semester.Color ?? string.Empty;  // set backing field directly — no callback on init
    }

    /// <summary>
    /// Syncs the model's <see cref="Semester.Color"/> and fires the persist callback
    /// whenever the user picks a new colour via the <c>ColorView</c>.
    /// </summary>
    partial void OnHexColorChanged(string value)
    {
        if (Semester.Color == value) return;
        Semester.Color = value;
        _onColorChanged(Semester);
    }
}

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

    private bool CanWrite() => _lockService.IsWriter && PlatformCapabilities.SupportsSemesterMutations;

    /// <summary>Semesters belonging to the academic year, ordered by SortOrder; wrapped in row VMs for color-picker binding.</summary>
    [ObservableProperty] private ObservableCollection<SemesterRowViewModel> _semesters = new();

    /// <summary>Currently selected semester row in the list.</summary>
    [ObservableProperty] private SemesterRowViewModel? _selectedSemester;

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

    /// <summary>
    /// Reloads the semester list from the database for this academic year,
    /// wrapping each <see cref="Semester"/> in a <see cref="SemesterRowViewModel"/>
    /// whose color-change callback persists the new color and refreshes the semester context.
    /// </summary>
    private void Load() =>
        Semesters = new ObservableCollection<SemesterRowViewModel>(
            _semRepo.GetByAcademicYear(_academicYearId)
                    .Select(s => new SemesterRowViewModel(s, OnSemesterColorChanged)));

    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Persists a semester's updated color to the database and reloads the semester context
    /// so the top-bar dropdowns and schedule grid reflect the new color immediately.
    /// Invoked by <see cref="SemesterRowViewModel"/> when the user picks a color.
    /// </summary>
    private void OnSemesterColorChanged(Semester semester)
    {
        try
        {
            _semRepo.Update(semester);
            _semesterContext.Reload(_ayRepo, _semRepo);
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SemesterManagerViewModel.OnSemesterColorChanged");
        }
    }

    partial void OnSelectedSemesterChanged(SemesterRowViewModel? value)
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
            list[i].Semester.SortOrder = i;

        foreach (var row in list)
            _semRepo.Update(row.Semester);

        var selectedId = item.Semester.Id;
        _semesterContext.Reload(_ayRepo, _semRepo);
        Load();
        SelectedSemester = Semesters.FirstOrDefault(s => s.Semester.Id == selectedId);
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    /// <summary>Opens the inline Add form for a new semester at the bottom of the list.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        LastErrorMessage = null;
        var semester = new Semester
        {
            AcademicYearId = _academicYearId,
            SortOrder      = Semesters.Count > 0 ? Semesters.Max(s => s.Semester.SortOrder) + 1 : 0
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
                    LastErrorMessage = "The save could not be completed. Please try again.";
                }
            },
            onCancel: () => EditVm = null);
    }

    /// <summary>Opens the inline Rename form for the currently selected semester.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Rename()
    {
        LastErrorMessage = null;
        if (SelectedSemester is null) return;
        // Work on a copy so Cancel leaves the original untouched.
        var copy = new Semester
        {
            Id             = SelectedSemester.Semester.Id,
            AcademicYearId = SelectedSemester.Semester.AcademicYearId,
            Name           = SelectedSemester.Semester.Name,
            SortOrder      = SelectedSemester.Semester.SortOrder,
            Color          = SelectedSemester.Semester.Color
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
                    LastErrorMessage = "The save could not be completed. Please try again.";
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

        LastErrorMessage = null;
        var count = _sectionRepo.CountBySemesterId(SelectedSemester.Semester.Id);
        if (count > 0)
        {
            LastErrorMessage =
                $"\"{SelectedSemester.Name}\" contains {count} section{(count == 1 ? "" : "s")}. " +
                "Use the Empty Semester tool to remove all sections before deleting a semester.";
            return;
        }

        if (!await _dialog.Confirm($"Delete semester \"{SelectedSemester.Name}\"?"))
            return;

        try
        {
            _semRepo.Delete(SelectedSemester.Semester.Id);
            _semesterContext.Reload(_ayRepo, _semRepo);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SemesterManagerViewModel.Delete");
            LastErrorMessage = "The delete could not be completed. Please try again.";
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
