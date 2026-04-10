using System.Windows.Input;

namespace SchedulingAssistant.Models;

/// <summary>
/// Represents a recent database file for display in the Files menu.
/// </summary>
public class RecentDatabaseItem
{
    /// <summary>
    /// Full file path to the database.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Display name (typically the filename with extension).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Command that opens this database. Pre-wired at construction time in MainWindowViewModel.
    /// </summary>
    public ICommand? OpenCommand { get; set; }
}
