using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

public partial class SemesterEditViewModel : UnappliedChangesEditor
{
    [ObservableProperty] private string _name = string.Empty;

    public string Title => IsNew ? "Add Semester" : "Edit Semester";
    public bool IsNew { get; }

    private readonly Semester _semester;
    private readonly Action<Semester> _onSave;
    private readonly Action _onCancel;

    public SemesterEditViewModel(Semester semester, bool isNew, Action<Semester> onSave, Action onCancel)
    {
        _semester = semester;
        IsNew = isNew;
        _onSave = onSave;
        _onCancel = onCancel;

        Name = semester.Name;

        TakeSnapshot();
    }

    /// <inheritdoc/>
    protected override string CaptureState() => UnappliedChangesKey.Of(Name.Trim());

    /// <inheritdoc/>
    protected override System.Windows.Input.ICommand ApplyCommand => SaveCommand;

    /// <inheritdoc/>
    protected override System.Windows.Input.ICommand CancelCommandForDiscard => CancelCommand;

    [RelayCommand]
    private void Save()
    {
        _semester.Name = Name.Trim();
        _onSave(_semester);
        MarkApplied();
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
