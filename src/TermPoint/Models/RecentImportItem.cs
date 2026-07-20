using System.Windows.Input;

namespace TermPoint.Models;

/// <summary>
/// Represents a recently imported shared-schedule CSV for display in the Sharing flyout's
/// Import section, allowing one-click re-import.
/// </summary>
public class RecentImportItem
{
    /// <summary>
    /// Full file path to the shared-schedule CSV.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Display name (the filename with extension). Full path is surfaced via tooltip.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Command that re-imports this file. Pre-wired at construction time in SharingViewModel.
    /// </summary>
    public ICommand? LoadCommand { get; set; }
}
