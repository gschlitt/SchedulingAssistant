using TermPoint.Models;

namespace TermPoint.Services.Access;

/// <summary>
/// One access watch's results for one semester: the watch (a watched tag), its analysis and its conflict marks.
/// The marks are computed with the snapshot; the analysis only when first asked for, because the grid needs the
/// marks alone and the Access tab (later) is the one that reads the analysis.
/// </summary>
public sealed class WatchAccess
{
    private readonly Lazy<Analysis> _analysis;

    /// <summary>The watched tag (its name is the watch's name, read live).</summary>
    public SchedulingEnvironmentValue Tag { get; }

    /// <summary>The watch the engine was given: the tag's id and Multi-campus box.</summary>
    public AccessWatch Watch { get; }

    /// <summary>The conflicts to mark for this watch over the whole semester (<see cref="ConflictMarks.For(AccessInputs, AccessWatch, bool)"/>).</summary>
    public IReadOnlyList<ConflictMark> Marks { get; }

    /// <summary>The watch's analysis over the whole semester, worked out on first use and kept.</summary>
    public Analysis Analysis => _analysis.Value;

    /// <summary>Creates the results of one watch.</summary>
    /// <param name="tag">The watched tag.</param>
    /// <param name="watch">The watch built from the tag.</param>
    /// <param name="marks">The watch's conflict marks.</param>
    /// <param name="analysis">Produces the watch's analysis when first asked for.</param>
    public WatchAccess(SchedulingEnvironmentValue tag, AccessWatch watch, IReadOnlyList<ConflictMark> marks, Func<Analysis> analysis)
    {
        Tag = tag;
        Watch = watch;
        Marks = marks;
        _analysis = new Lazy<Analysis>(analysis);
    }
}

/// <summary>One loaded semester's access results: its inputs and the results of every watched tag.</summary>
/// <param name="SemesterId">The semester's id.</param>
/// <param name="Inputs">The semester's inputs (the saved sections, no proposal).</param>
/// <param name="Watches">One entry per watched tag, in the tags' sort order.</param>
public sealed record SemesterAccess(string SemesterId, AccessInputs Inputs, IReadOnlyList<WatchAccess> Watches);

/// <summary>
/// What <see cref="AccessState"/> hands out: the results for every loaded semester, each analysed on its own
/// (decision Q43). Immutable; a recompute produces a new snapshot.
/// </summary>
public sealed class AccessSnapshot
{
    /// <summary>A snapshot of nothing (no semester loaded).</summary>
    public static AccessSnapshot Empty { get; } = new(new Dictionary<string, SemesterAccess>());

    /// <summary>The results by semester id.</summary>
    public IReadOnlyDictionary<string, SemesterAccess> Semesters { get; }

    /// <summary>Creates a snapshot.</summary>
    /// <param name="semesters">The results by semester id.</param>
    public AccessSnapshot(IReadOnlyDictionary<string, SemesterAccess> semesters) => Semesters = semesters;
}
