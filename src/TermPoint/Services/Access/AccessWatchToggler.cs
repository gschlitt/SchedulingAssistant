using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// Turns a tag's access watch on or off: the eye in the filter's Tags popup and in Scheduling Environment, Tags.
/// A watch is a tag with <see cref="SchedulingEnvironmentValue.IsAccessWatch"/> set, so this is an ordinary save of
/// one scheduling-environment value, made exactly as the Scheduling Environment list saves an edited value:
/// <see cref="ISchedulingEnvironmentRepository.Update"/>, which marks the database dirty so the checkout service
/// writes it back. Turning a watch off leaves the tag and its sections alone.
///
/// <para><b>Write lock.</b> Only the computer that holds the write lock may change a tag. <see cref="CanToggle"/>
/// says whether that is so now; the views disable the eye with it, and <see cref="SetWatched"/> refuses when it is
/// false, so a click that races a lost lock saves nothing.</para>
///
/// <para>After a save the access state is made stale and the grid is told its content changed (both raise the
/// Access tab's refresh), so the new watch's marks and health line appear without a section reload.</para>
/// </summary>
public sealed class AccessWatchToggler
{
    private readonly ISchedulingEnvironmentRepository _envRepo;
    private readonly AccessState _state;
    private readonly GridChangeNotifier _notifier;
    private readonly Func<bool> _canWrite;

    /// <summary>Raised when <see cref="CanToggle"/> may have changed (the write lock was gained or lost).</summary>
    public event Action? CanToggleChanged;

    /// <summary>Creates the toggler for the application.</summary>
    /// <param name="envRepo">Reads and saves the tags.</param>
    /// <param name="state">Made stale after a change.</param>
    /// <param name="notifier">Tells the grid its content changed.</param>
    /// <param name="lockService">Says whether this computer holds the write lock.</param>
    public AccessWatchToggler(
        ISchedulingEnvironmentRepository envRepo, AccessState state, GridChangeNotifier notifier, WriteLockService lockService)
        : this(envRepo, state, notifier, () => lockService.IsWriter)
    {
        lockService.LockStateChanged += () => CanToggleChanged?.Invoke();
    }

    /// <summary>Creates a toggler with its own write test (the tests' seam).</summary>
    /// <param name="envRepo">Reads and saves the tags.</param>
    /// <param name="state">Made stale after a change.</param>
    /// <param name="notifier">Tells the grid its content changed.</param>
    /// <param name="canWrite">Whether this computer may save now.</param>
    internal AccessWatchToggler(
        ISchedulingEnvironmentRepository envRepo, AccessState state, GridChangeNotifier notifier, Func<bool> canWrite)
    {
        _envRepo = envRepo;
        _state = state;
        _notifier = notifier;
        _canWrite = canWrite;
    }

    /// <summary>Whether a watch may be turned on or off now: this computer holds the write lock.</summary>
    public bool CanToggle => _canWrite();

    /// <summary>
    /// Sets whether a tag is an access watch and saves the tag. Does nothing when the tag is gone or already has
    /// that setting (nothing is saved, so the database is not marked dirty for no reason).
    /// </summary>
    /// <param name="tagId">The tag's id.</param>
    /// <param name="watched">True to watch the tag, false to stop watching it.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours (<see cref="CanToggle"/> is false); nothing is saved.</exception>
    public void SetWatched(string tagId, bool watched)
    {
        if (!CanToggle)
            throw new InvalidOperationException("An access watch can only be changed while this computer can save.");
        var tag = _envRepo.GetById(tagId);
        if (tag is null || tag.IsAccessWatch == watched) return;
        tag.IsAccessWatch = watched; // the record read back keeps unknown JSON fields (PersistedRecord)
        _envRepo.Update(tag);
        _state.Invalidate();
        _notifier.NotifyGridContentChanged();
    }

    /// <summary>
    /// Sets a watch's Multi-campus box (decision 6e) and saves the tag: one <see cref="ISchedulingEnvironmentRepository.Update"/>,
    /// as <see cref="SetWatched"/>, then the access state is made stale and the grid told. Does nothing when the tag is
    /// gone or already has that setting. Like the eye, it is refused without the write lock.
    /// </summary>
    /// <param name="tagId">The watched tag's id.</param>
    /// <param name="multiCampus">True to let slates mix campuses, false for the one-campus rule.</param>
    /// <exception cref="InvalidOperationException">The write lock is not ours (<see cref="CanToggle"/> is false); nothing is saved.</exception>
    public void SetMultiCampus(string tagId, bool multiCampus)
    {
        if (!CanToggle)
            throw new InvalidOperationException("An access watch can only be changed while this computer can save.");
        var tag = _envRepo.GetById(tagId);
        if (tag is null || tag.WatchMultiCampus == multiCampus) return;
        tag.WatchMultiCampus = multiCampus; // the record read back keeps unknown JSON fields (PersistedRecord)
        _envRepo.Update(tag);
        _state.Invalidate();
        _notifier.NotifyGridContentChanged();
    }
}
