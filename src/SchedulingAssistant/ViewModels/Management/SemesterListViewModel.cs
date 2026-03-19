using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SemesterListViewModel : ViewModelBase
{
    private readonly ISemesterRepository _repo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    [ObservableProperty] private ObservableCollection<Semester> _semesters = new();
    [ObservableProperty] private Semester? _selectedSemester;
    [ObservableProperty] private SemesterEditViewModel? _editVm;

    /// <summary>True when the current user holds the write lock; controls whether CRUD buttons are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    public SemesterListViewModel(ISemesterRepository repo, IDialogService dialog, WriteLockService lockService)
    {
        _repo = repo;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load() =>
        Semesters = new ObservableCollection<Semester>(_repo.GetAll());

    /// <summary>
    /// Called when the write lock state changes. Notifies the UI to re-evaluate
    /// <see cref="IsWriteEnabled"/> and all write command CanExecute states.
    /// </summary>
    private void OnLockStateChanged()
    {
        OnPropertyChanged(nameof(IsWriteEnabled));
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Add()
    {
        var semester = new Semester();
        EditVm = new SemesterEditViewModel(semester, isNew: true,
            onSave: s =>
            {
                try { _repo.Insert(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SemesterListViewModel.Add"); _ = _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedSemester is null) return;
        var copy = new Semester { Id = SelectedSemester.Id, Name = SelectedSemester.Name, SortOrder = SelectedSemester.SortOrder };
        EditVm = new SemesterEditViewModel(copy, isNew: false,
            onSave: s =>
            {
                try { _repo.Update(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SemesterListViewModel.Edit"); _ = _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedSemester is null) return;
        try
        {
            _repo.Delete(SelectedSemester.Id);
            Load();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "SemesterListViewModel.Delete");
            await _dialog.ShowError("The delete could not be completed. Please try again.");
        }
    }
}
