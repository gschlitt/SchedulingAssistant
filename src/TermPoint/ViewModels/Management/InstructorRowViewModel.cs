using TermPoint.Models;

namespace TermPoint.ViewModels.Management;

/// <summary>
/// One row of the Instructors flyout's list: an <see cref="Instructor"/> plus the presentation state the list needs
/// under a Subjects focus (spec item 20, step 6). The persisted <see cref="Instructor"/> model must not carry
/// display-only properties, so they live here; the flyout view model keeps selecting, editing and deleting
/// <see cref="Value"/> exactly as before and only the list itself binds to these rows. Built once per list rebuild
/// and never changed afterwards, so it needs no change notification.
/// </summary>
public sealed class InstructorRowViewModel
{
    /// <summary>The instructor this row shows. The same object the flyout selects, edits and deletes.</summary>
    public Instructor Value { get; }

    /// <summary>
    /// True when the instructor is outside the chosen subjects: the row's text shows in the lighter out-of-focus
    /// colour, under the "Other instructors" heading. Nobody is hidden; the row stays selectable and editable.
    /// </summary>
    public bool IsOutOfFocus { get; }

    /// <summary>
    /// True on exactly one row, the first out-of-focus instructor that follows at least one in-focus one: the row
    /// that carries the "Other instructors" heading and thin rule above it.
    /// </summary>
    public bool ShowOthersHeading { get; }

    /// <summary>First name, for the First column.</summary>
    public string FirstName => Value.FirstName;

    /// <summary>Last name, for the Last column.</summary>
    public string LastName => Value.LastName;

    /// <summary>Initials, for the Init column.</summary>
    public string Initials => Value.Initials;

    /// <summary>Staff type name (resolved at load time), for the Type column; null when the instructor has none.</summary>
    public string? StaffTypeName => Value.StaffTypeName;

    /// <summary>Email address, for the Email column.</summary>
    public string Email => Value.Email;

    /// <summary>Whether the instructor is active, for the Active column.</summary>
    public bool IsActive => Value.IsActive;

    /// <summary>Creates a row.</summary>
    /// <param name="value">The instructor the row shows.</param>
    /// <param name="isOutOfFocus">Whether the instructor is outside the chosen subjects (dimmed).</param>
    /// <param name="showOthersHeading">Whether this row carries the "Other instructors" heading.</param>
    public InstructorRowViewModel(Instructor value, bool isOutOfFocus, bool showOthersHeading)
    {
        Value = value;
        IsOutOfFocus = isOutOfFocus;
        ShowOthersHeading = showOthersHeading;
    }
}
