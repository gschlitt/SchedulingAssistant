using CommunityToolkit.Mvvm.ComponentModel;

namespace TermPoint.ViewModels.GridView;

/// <summary>One checkable or selectable row in a schedule-grid tile's right-click menu (instructors, rooms, tags, flags).</summary>
public partial class ContextMenuItemVm : ObservableObject
{
    public string Id { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    /// <summary>
    /// True for an instructor outside the chosen subjects (spec item 20): the menu shows the name in a lighter colour,
    /// under the "Other instructors" heading. Set at construction; purely presentational.
    /// </summary>
    public bool IsOutOfFocus { get; }

    /// <summary>
    /// True on exactly one row, the first out-of-focus instructor that follows at least one in-focus one: the row
    /// that carries the "Other instructors" heading above it. Set at construction.
    /// </summary>
    public bool ShowOthersHeading { get; }

    /// <summary>Creates a menu row.</summary>
    /// <param name="id">The id of the instructor, room, tag or flag the row stands for.</param>
    /// <param name="label">The text shown.</param>
    /// <param name="isChecked">Whether the row starts ticked.</param>
    /// <param name="isOutOfFocus">Whether to dim the label (an instructor outside the chosen subjects).</param>
    /// <param name="showOthersHeading">Whether this row carries the "Other instructors" heading.</param>
    public ContextMenuItemVm(
        string id, string label, bool isChecked = false, bool isOutOfFocus = false, bool showOthersHeading = false)
    {
        Id = id;
        Label = label;
        _isChecked = isChecked;
        IsOutOfFocus = isOutOfFocus;
        ShowOthersHeading = showOthersHeading;
    }
}
