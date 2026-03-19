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
    private readonly Func<AcademicYear, Task>? _onSaveAsync;
    private readonly Action<AcademicYear>? _onSave;
    private readonly Action _onCancel;
    private readonly Func<string, bool> _nameExists;

    private static readonly Regex YearPattern = new(@"^\d{4}$", RegexOptions.Compiled);

    /// <summary>
    /// Expands a 4-digit start year entered by the user into the canonical "YYYY-YYYY+1" name.
    /// Returns the expanded name, or the original trimmed input if it is not a bare 4-digit year.
    /// </summary>
    private static string ExpandName(string raw)
    {
        var trimmed = raw.Trim();
        if (YearPattern.IsMatch(trimmed))
        {
            var y1 = int.Parse(trimmed);
            return $"{y1}-{y1 + 1}";
        }
        return trimmed;
    }

    public string? ValidationError
    {
        get
        {
            var trimmed = Name.Trim();
            if (trimmed.Length == 0)
                return null; // no message while field is still empty

            // Accept a bare 4-digit year; anything else must be the expanded form.
            if (!YearPattern.IsMatch(trimmed) && !(trimmed.Length == 9 && trimmed[4] == '-'))
                return "Enter the calendar year in which the academic year begins (e.g. 2024).";

            var expanded = ExpandName(trimmed);

            // After expansion, validate the YYYY-YYYY+1 structure.
            var y1 = int.Parse(expanded[..4]);
            var y2 = int.Parse(expanded[5..]);
            if (y2 != y1 + 1)
                return $"The second year must be {y1 + 1}.";

            if (_nameExists(expanded))
                return $"{expanded} already exists.";

            return null;
        }
    }

    private bool CanSave() => Name.Trim().Length > 0 && ValidationError is null;

    public AcademicYearEditViewModel(
        AcademicYear academicYear,
        Action<AcademicYear>? onSave = null,
        Action? onCancel = null,
        Func<string, bool>? nameExists = null,
        Func<AcademicYear, Task>? onSaveAsync = null)
    {
        _academicYear = academicYear;
        _onSave = onSave;
        _onSaveAsync = onSaveAsync;
        _onCancel = onCancel ?? (() => { });
        _nameExists = nameExists ?? (_ => false);

        Name = academicYear.Name;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        _academicYear.Name = ExpandName(Name);
        if (_onSaveAsync is not null)
        {
            await _onSaveAsync(_academicYear);
        }
        else if (_onSave is not null)
        {
            _onSave(_academicYear);
        }
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
