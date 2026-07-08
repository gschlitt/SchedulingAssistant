using CommunityToolkit.Mvvm.ComponentModel;
using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// Represents one row in a mapping confirmation table — a distinct CSV value
/// paired with the database record it maps to. Used for subject mapping (course import)
/// and environment mapping (section import: rooms, section types, campuses, meeting types).
/// </summary>
public partial class MappingEntryViewModel : ObservableObject
{
    /// <summary>The raw value found in the CSV (e.g. "CHEM", "MAC 310", "Lecture").</summary>
    public string CsvValue { get; }

    /// <summary>The auto-match status determined by <see cref="Services.CsvImportMatcher"/>.</summary>
    public MatchStatus AutoMatchStatus { get; }

    /// <summary>
    /// Available database records the operator can pick from. Includes all records of
    /// the relevant type plus a null sentinel for "Skip (leave blank)".
    /// </summary>
    public List<EnvironmentTarget> AvailableOptions { get; }

    /// <summary>Display labels for the ComboBox, index-aligned with <see cref="AvailableOptions"/>.</summary>
    public List<string> OptionLabels { get; }

    /// <summary>
    /// Index of the currently selected option in <see cref="AvailableOptions"/>.
    /// -1 = nothing selected (unresolved). The last index = "Skip (leave blank)".
    /// </summary>
    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>
    /// The resolved database record, or null if skipped / not yet selected.
    /// Updated automatically when <see cref="SelectedIndex"/> changes.
    /// </summary>
    public EnvironmentTarget? ResolvedTarget => SelectedIndex >= 0 && SelectedIndex < AvailableOptions.Count
        ? AvailableOptions[SelectedIndex]
        : null;

    /// <summary>True when the operator has made a selection (or auto-match was applied).</summary>
    public bool IsResolved => SelectedIndex >= 0;

    /// <param name="csvValue">The raw CSV value for this mapping row.</param>
    /// <param name="autoMatch">The match result from CsvImportMatcher.</param>
    /// <param name="allOptions">All database records of this type, for ComboBox population.</param>
    public MappingEntryViewModel(string csvValue, MatchResult<EnvironmentTarget> autoMatch, List<EnvironmentTarget> allOptions)
    {
        CsvValue = csvValue;
        AutoMatchStatus = autoMatch.Status;

        // Build the option list: all DB records + a "Skip" sentinel.
        AvailableOptions = new List<EnvironmentTarget>(allOptions);
        OptionLabels = allOptions.Select(o => o.DisplayName).Append("Skip (leave blank)").ToList();

        // Pre-select the auto-matched entry if exact.
        if (autoMatch.Status == MatchStatus.Exact && autoMatch.Resolved is not null)
        {
            var idx = allOptions.FindIndex(o => o.Id == autoMatch.Resolved.Id);
            if (idx >= 0)
                SelectedIndex = idx;
        }
    }

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(IsResolved));
}
