using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Models;
using System.Text.RegularExpressions;

namespace SchedulingAssistant.ViewModels.Management;

public partial class AcademicYearEditViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    private readonly AcademicYear _academicYear;
    private readonly Action<AcademicYear> _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _nameExists;

    private static readonly Regex NamePattern = new(@"^\d{4}-\d{4}$", RegexOptions.Compiled);

    public string? ValidationError
    {
        get
        {
            var trimmed = Name.Trim();
            if (trimmed.Length == 0)
                return null; // no message while field is still empty

            if (!NamePattern.IsMatch(trimmed))
                return "Format must be YYYY-YYYY (e.g. 2024-2025).";

            var y1 = int.Parse(trimmed[..4]);
            var y2 = int.Parse(trimmed[5..]);
            if (y2 != y1 + 1)
                return $"The second year must be {y1 + 1}.";

            if (_nameExists(trimmed))
                return $"{trimmed} already exists.";

            return null;
        }
    }

    private bool CanSave() => Name.Trim().Length > 0 && ValidationError is null;

    public AcademicYearEditViewModel(
        AcademicYear academicYear,
        Action<AcademicYear> onSave,
        Action onCancel,
        Func<string, bool> nameExists)
    {
        _academicYear = academicYear;
        _onSave = onSave;
        _onCancel = onCancel;
        _nameExists = nameExists;

        Name = academicYear.Name;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        _academicYear.Name = Name.Trim();
        _onSave(_academicYear);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
