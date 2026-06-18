namespace TermPoint.Services;

/// <summary>
/// Abstracts the app actions that tour step PreAction/PostAction callbacks need.
/// Implemented by MainWindowViewModel (or a thin adapter) in the presentation layer.
/// Enables unit testing of tour action logic without a real UI.
/// </summary>
public interface ITourActionContext
{
    /// <summary>Opens a management flyout by name (e.g. "Rooms", "Instructors").</summary>
    /// <param name="name">The flyout identifier matching the navigation button name.</param>
    Task OpenFlyout(string name);

    /// <summary>Closes any open management flyout.</summary>
    Task CloseFlyout();

    /// <summary>Selects a section by ID in the section list, highlighting it on the grid.</summary>
    /// <param name="id">The section's unique identifier.</param>
    Task SelectSection(string id);

    /// <summary>Applies a filter to the schedule grid.</summary>
    /// <param name="filterType">The filter category (e.g. "Instructor", "Room").</param>
    /// <param name="value">The value to filter by.</param>
    Task ApplyFilter(string filterType, string value);

    /// <summary>Clears all active grid filters.</summary>
    Task ClearFilter();
}
