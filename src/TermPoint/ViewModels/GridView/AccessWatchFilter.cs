using TermPoint.Services.Access;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// Opening an access watch filters the grid for real (Greg, 2026-11-25): the filter bar is set to the watch's tag
/// (Tags: Cohort 1), or to the course for a course checked alone, so the header says what is shown and the user can
/// narrow it with other filters and overlays as usual. A watch is a tag (D28), so the tag filter shows exactly the
/// watch's sections.
///
/// <para><b>The user's own filters.</b> Opening remembers them and replaces them (adding the tag on top of, say, a
/// Campus filter could hide some of the watch's sections with no one noticing). Closing puts them back only while the
/// filter is still just what opening set; if the user changed the filters while the watch was open, they are now
/// theirs and closing leaves them. Opening another watch while one is open, with the filters untouched, swaps the tag
/// and keeps the remembered filters.</para>
///
/// <para>Runs on <see cref="AccessPicture.Changed"/>; only a change of what is open (a watch, a course, nothing)
/// acts. Ticks, pins, stepping and semester switches leave the filters alone.</para>
/// </summary>
public sealed class AccessWatchFilter
{
    private readonly AccessPicture _picture;
    private readonly GridFilterViewModel _filter;

    // What is open now ("tag:id", "course:id" or null), the filters before opening, and what opening set.
    private string? _openKey;
    private FilterSelection? _saved;
    private FilterSelection? _applied;

    /// <summary>Creates the coordinator and subscribes to the picture.</summary>
    /// <param name="picture">The open watch.</param>
    /// <param name="filter">The grid's filter bar.</param>
    public AccessWatchFilter(AccessPicture picture, GridFilterViewModel filter)
    {
        _picture = picture;
        _filter = filter;
        // Never unsubscribed: both live as long as the grid view model (the services of one database).
        _picture.Changed += OnPictureChanged;
    }

    /// <summary>Sets or restores the filters when what is open changed.</summary>
    private void OnPictureChanged()
    {
        string? key = !_picture.IsOpen ? null
            : _picture.IsCourseOnly ? $"course:{_picture.CourseId}"
            : $"tag:{_picture.TagId}";
        if (key == _openKey) return;
        var was = _openKey;
        _openKey = key;
        try
        {
            bool untouched = _applied is not null && _applied.SameAs(_filter.CaptureSelection());
            if (key is null)
            {
                // Closed: give the user's filters back, unless they made the filters their own meanwhile.
                if (untouched && _saved is not null) _filter.ApplySelection(_saved);
                _saved = _applied = null;
                return;
            }
            // Opened, or switched to another watch: remember the user's filters unless what's there is still ours.
            if (was is null || !untouched) _saved = _filter.CaptureSelection();
            _applied = _picture.IsCourseOnly
                ? _filter.SelectionOf([], [_picture.CourseId!])
                : _filter.SelectionOf([_picture.TagId!], []);
            _filter.ApplySelection(_applied);
            // Read back: an id missing from a list is skipped, so compare later against what really took.
            _applied = _filter.CaptureSelection();
        }
        catch (Exception ex)
        {
            App.Logger.LogError(ex, "AccessWatchFilter.OnPictureChanged");
        }
    }
}
