using TermPoint.Data.Repositories;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Singleton service that holds the in-memory cache of sections for the currently selected
/// semester(s) and provides the single source of truth for cross-view section selection.
///
/// <para>
/// All ViewModels that display sections (Section List, Schedule Grid, Workload Panel) read
/// from this cache instead of querying the database independently. After any write operation
/// (insert, update, delete), the caller invokes <see cref="Reload"/> once; all subscribers
/// receive <see cref="SectionsChanged"/> and re-derive their view-specific representations
/// from the updated cache.
/// </para>
///
/// <para>
/// Selection is also centralised here. Calling <see cref="SetSelection"/>,
/// <see cref="ToggleSelection"/>, or <see cref="SetMultiSelection"/> propagates a single
/// <see cref="SelectionChanged"/> notification to every subscriber, removing the need for
/// direct ViewModel-to-ViewModel references and suppress-flag patterns.
/// </para>
///
/// <para>
/// Following the pattern used by <see cref="SemesterContext.Reload"/>, the
/// <see cref="SectionRepository"/> is passed into <see cref="Reload"/> by the caller rather
/// than held as a constructor field. This keeps the singleton free of transient-in-singleton
/// scoping concerns.
/// </para>
/// </summary>
public class SectionStore
{
    private IReadOnlyDictionary<string, IReadOnlyList<Section>> _sectionsBySemester
        = new Dictionary<string, IReadOnlyList<Section>>();

    // ── Cached Data ────────────────────────────────────────────────────────────

    /// <summary>
    /// All sections across every currently loaded semester, as a flat list.
    /// Rebuilt on every <see cref="Reload"/> call.
    /// </summary>
    public IReadOnlyList<Section> Sections { get; private set; } = [];

    /// <summary>
    /// Sections grouped by semester ID.
    /// Callers that need per-semester breakdown (e.g. the Workload Panel and Schedule Grid
    /// in multi-semester mode) should use this dictionary.
    /// Rebuilt on every <see cref="Reload"/> call.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Section>> SectionsBySemester
        => _sectionsBySemester;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired synchronously at the end of every successful <see cref="Reload"/> call.
    /// Subscribers should re-derive their view-specific representations from
    /// <see cref="Sections"/> or <see cref="SectionsBySemester"/>.
    /// </summary>
    public event Action? SectionsChanged;

    /// <summary>
    /// Fired whenever <see cref="SelectedSectionIds"/> changes.
    /// The new set of selected section IDs is passed as the argument; never null (may be empty).
    /// </summary>
    public event Action<IReadOnlySet<string>>? SelectionChanged;

    /// <summary>
    /// Fired whenever <see cref="FilteredSectionIds"/> is updated by
    /// <see cref="SetFilteredSectionIds"/>.
    /// </summary>
    public event Action? FilteredIdsChanged;

    // ── Filter highlight ───────────────────────────────────────────────────────

    /// <summary>
    /// The set of section IDs that currently pass the active Schedule Grid filter, or
    /// <c>null</c> when no regular (non-overlay) filter is active.
    /// <list type="bullet">
    ///   <item><c>null</c> — no filter is active; the section list shows no highlight borders.</item>
    ///   <item>Non-null (possibly empty) — a filter is active; only IDs in the set are highlighted.</item>
    /// </list>
    /// Updated by <see cref="SetFilteredSectionIds"/> after every grid reload.
    /// </summary>
    public IReadOnlySet<string>? FilteredSectionIds { get; private set; }

    // ── Selection ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The set of currently selected section IDs. Empty when nothing is selected.
    /// Use <see cref="SetSelection"/>, <see cref="ToggleSelection"/>, or
    /// <see cref="SetMultiSelection"/> to change this — do not set it directly.
    /// </summary>
    public IReadOnlySet<string> SelectedSectionIds { get; private set; } = new HashSet<string>();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reloads sections from the database for the given semester IDs, replaces the
    /// in-memory cache, and fires <see cref="SectionsChanged"/>.
    /// </summary>
    /// <param name="sectionRepo">
    /// A <see cref="SectionRepository"/> for this call. The caller is responsible for
    /// providing a repository instance; the store does not hold one of its own.
    /// </param>
    /// <param name="semesterIds">
    /// The IDs of all semesters to load. Passing an empty sequence is valid and
    /// produces an empty cache.
    /// </param>
    public void Reload(ISectionRepository sectionRepo, IEnumerable<string> semesterIds)
    {
        var dict = new Dictionary<string, IReadOnlyList<Section>>();
        foreach (var semId in semesterIds)
            dict[semId] = sectionRepo.GetAll(semId).AsReadOnly();

        _sectionsBySemester = dict;
        Sections = dict.Values.SelectMany(v => v).ToList();
        SectionsChanged?.Invoke();
    }

    /// <summary>
    /// Sets <see cref="FilteredSectionIds"/> and fires <see cref="FilteredIdsChanged"/>.
    /// Called by <see cref="ScheduleGridViewModel"/> after every filter rebuild.
    /// </summary>
    /// <param name="ids">
    /// The section IDs that pass the current filter, or <c>null</c> when no regular filter
    /// is active. Pass an empty set when a filter is active but nothing matches.
    /// </param>
    public void SetFilteredSectionIds(IReadOnlySet<string>? ids)
    {
        FilteredSectionIds = ids;
        FilteredIdsChanged?.Invoke();
    }

    /// <summary>
    /// Replaces the selection with a single section and fires <see cref="SelectionChanged"/>.
    /// Clears the selection when <paramref name="sectionId"/> is <c>null</c>.
    /// Does nothing if the set already equals {<paramref name="sectionId"/>}, preventing
    /// echo-back loops between subscribers.
    /// </summary>
    /// <param name="sectionId">The section to select, or <c>null</c> to deselect all.</param>
    public void SetSelection(string? sectionId)
    {
        var newSet = sectionId is null
            ? (IReadOnlySet<string>)new HashSet<string>()
            : new HashSet<string> { sectionId };
        if (SetsEqual(SelectedSectionIds, newSet)) return;
        SelectedSectionIds = newSet;
        SelectionChanged?.Invoke(SelectedSectionIds);
    }

    /// <summary>
    /// Adds <paramref name="sectionId"/> to the selection if absent, or removes it if
    /// already present, then fires <see cref="SelectionChanged"/>.
    /// Used for Ctrl+Click multi-select across all panels.
    /// </summary>
    /// <param name="sectionId">The section ID to toggle.</param>
    public void ToggleSelection(string sectionId)
    {
        var newSet = new HashSet<string>(SelectedSectionIds);
        if (!newSet.Add(sectionId))
            newSet.Remove(sectionId);
        // SetsEqual will always be false here (we always changed the set), but guard anyway.
        if (SetsEqual(SelectedSectionIds, newSet)) return;
        SelectedSectionIds = newSet;
        SelectionChanged?.Invoke(SelectedSectionIds);
    }

    /// <summary>
    /// Replaces the selection with an arbitrary set of section IDs and fires
    /// <see cref="SelectionChanged"/>. Does nothing when the new set equals the current one.
    /// Intended for bulk-selection operations such as "highlight all sections for an instructor".
    /// </summary>
    /// <param name="sectionIds">The section IDs to select. Duplicates are silently removed.</param>
    public void SetMultiSelection(IEnumerable<string> sectionIds)
    {
        var newSet = new HashSet<string>(sectionIds);
        if (SetsEqual(SelectedSectionIds, newSet)) return;
        SelectedSectionIds = newSet;
        SelectionChanged?.Invoke(SelectedSectionIds);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool SetsEqual(IReadOnlySet<string> a, IReadOnlySet<string> b) =>
        a.Count == b.Count && a.SetEquals(b);
}
