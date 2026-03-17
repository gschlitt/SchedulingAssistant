using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using System.Collections.ObjectModel;

namespace SchedulingAssistant.ViewModels.Management;

public partial class SubjectListViewModel : ViewModelBase
{
    private readonly SubjectRepository _repo;
    private readonly IDialogService _dialog;
    private readonly WriteLockService _lockService;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private SubjectEditViewModel? _editVm;

    /// <summary>True when the current user holds the write lock; controls whether CRUD buttons are enabled.</summary>
    public bool IsWriteEnabled => _lockService.IsWriter;

    /// <summary>Returns true when the current user holds the write lock. Used as a CanExecute predicate for all write commands.</summary>
    private bool CanWrite() => _lockService.IsWriter;

    public SubjectListViewModel(SubjectRepository repo, IDialogService dialog, WriteLockService lockService)
    {
        _repo = repo;
        _dialog = dialog;
        _lockService = lockService;
        _lockService.LockStateChanged += OnLockStateChanged;
        Load();
    }

    private void Load()
    {
        Subjects = new ObservableCollection<Subject>(_repo.GetAll());
        SelectedSubject = null;
    }

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
        var subject = new Subject();
        EditVm = new SubjectEditViewModel(subject, isNew: true,
            onSave: async s =>
            {
                try { _repo.Insert(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SubjectListViewModel.Add"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(name),
            abbreviationExists: abbr => _repo.ExistsByAbbreviation(abbr));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private void Edit()
    {
        if (SelectedSubject is null) return;
        var clone = new Subject
        {
            Id = SelectedSubject.Id,
            Name = SelectedSubject.Name,
            CalendarAbbreviation = SelectedSubject.CalendarAbbreviation
        };
        EditVm = new SubjectEditViewModel(clone, isNew: false,
            onSave: async s =>
            {
                try { _repo.Update(s); Load(); EditVm = null; }
                catch (Exception ex) { App.Logger.LogError(ex, "SubjectListViewModel.Edit"); await _dialog.ShowError("The save could not be completed. Please try again."); }
            },
            onCancel: () => EditVm = null,
            nameExists: name => _repo.ExistsByName(name, excludeId: clone.Id),
            abbreviationExists: abbr => _repo.ExistsByAbbreviation(abbr, excludeId: clone.Id));
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task Delete()
    {
        if (SelectedSubject is null) return;

        if (_repo.HasCourses(SelectedSubject.Id))
        {
            await _dialog.ShowError($"Cannot delete \"{SelectedSubject.Name}\" — it has courses. Remove all courses from this subject first.");
            return;
        }

        _repo.Delete(SelectedSubject.Id);
        Load();
    }
}
