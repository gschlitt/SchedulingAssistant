using CommunityToolkit.Mvvm.ComponentModel;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.ViewModels.Management;

public partial class ReserveSelectionViewModel : ObservableObject
{
    public SectionPropertyValue Value { get; }
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Count as a user-editable string. Validated on save to a positive integer.
    /// </summary>
    [ObservableProperty] private string _codeText = "1";

    /// <summary>Parsed count, or null if the text is not a valid positive integer.</summary>
    public int? ParsedCode =>
        int.TryParse(CodeText?.Trim(), out var v) && v >= 1 ? v : null;

    public ReserveSelectionViewModel(SectionPropertyValue value, int? existingCode)
    {
        Value = value;
        _isSelected = existingCode.HasValue;
        _codeText = existingCode.HasValue ? existingCode.Value.ToString() : "1";
    }
}
