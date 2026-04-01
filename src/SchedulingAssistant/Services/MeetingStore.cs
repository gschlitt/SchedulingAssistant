using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Singleton service that holds the in-memory cache of meetings for the currently
/// selected semester(s). Parallel to <see cref="SectionStore"/> in structure and usage.
///
/// <para>
/// All ViewModels that display meetings (Meeting List, Schedule Grid) read from this
/// cache instead of querying the database independently. After any write operation
/// (insert, update, delete), the caller invokes <see cref="Reload"/> once; all
/// subscribers receive <see cref="MeetingsChanged"/> and re-derive their view-specific
/// representations from the updated cache.
/// </para>
///
/// <para>
/// Following the pattern used by <see cref="SectionStore"/>, the
/// <see cref="IMeetingRepository"/> is passed into <see cref="Reload"/> by the caller
/// rather than held as a constructor field. This keeps the singleton free of
/// transient-in-singleton scoping concerns.
/// </para>
/// </summary>
public class MeetingStore
{
    private IReadOnlyDictionary<string, IReadOnlyList<Meeting>> _meetingsBySemester
        = new Dictionary<string, IReadOnlyList<Meeting>>();

    // ── Cached Data ────────────────────────────────────────────────────────────

    /// <summary>
    /// All meetings across every currently loaded semester, as a flat list.
    /// Rebuilt on every <see cref="Reload"/> call.
    /// </summary>
    public IReadOnlyList<Meeting> Meetings { get; private set; } = [];

    /// <summary>
    /// Meetings grouped by semester ID.
    /// Callers that need per-semester breakdown should use this dictionary.
    /// Rebuilt on every <see cref="Reload"/> call.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Meeting>> MeetingsBySemester
        => _meetingsBySemester;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired synchronously at the end of every successful <see cref="Reload"/> call.
    /// Subscribers should re-derive their view-specific representations from
    /// <see cref="Meetings"/> or <see cref="MeetingsBySemester"/>.
    /// </summary>
    public event Action? MeetingsChanged;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reloads meetings from the database for the given semester IDs, replaces the
    /// in-memory cache, and fires <see cref="MeetingsChanged"/>.
    /// </summary>
    /// <param name="meetingRepo">
    /// A <see cref="IMeetingRepository"/> for this call. The caller is responsible for
    /// providing a repository instance; the store does not hold one of its own.
    /// </param>
    /// <param name="semesterIds">
    /// The IDs of all semesters to load. Passing an empty sequence is valid and
    /// produces an empty cache.
    /// </param>
    public void Reload(IMeetingRepository meetingRepo, IEnumerable<string> semesterIds)
    {
        var dict = new Dictionary<string, IReadOnlyList<Meeting>>();
        foreach (var semId in semesterIds)
            dict[semId] = meetingRepo.GetAll(semId).AsReadOnly();

        _meetingsBySemester = dict;
        Meetings = dict.Values.SelectMany(v => v).ToList();
        MeetingsChanged?.Invoke();
    }
}
