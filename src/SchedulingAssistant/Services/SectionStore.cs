using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

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
/// Selection is also centralised here. Calling <see cref="SetSelection"/> propagates a
/// single <see cref="SelectionChanged"/> notification to every subscriber, removing the
/// need for direct ViewModel-to-ViewModel references and suppress-flag patterns.
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
    /// Fired whenever <see cref="SelectedSectionId"/> changes.
    /// The new section ID (or <c>null</c> for a deselect) is passed as the argument.
    /// </summary>
    public event Action<string?>? SelectionChanged;

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
    /// The ID of the currently selected section, or <c>null</c> when nothing is selected.
    /// Use <see cref="SetSelection"/> to change this value — do not set it directly.
    /// </summary>
    public string? SelectedSectionId { get; private set; }

    // ── Save-flash coordination ────────────────────────────────────────────────

    /// <summary>
    /// The ID of the section that was just saved, set for the duration of a
    /// <see cref="ReloadAfterSave"/> call and cleared immediately after all
    /// <see cref="SectionsChanged"/> subscribers have returned.
    /// <para>
    /// Subscribers (e.g. <c>ScheduleGridViewModel</c>) read this during their
    /// <see cref="SectionsChanged"/> handler to know whether to start the
    /// apply-save flash animation.  It is <c>null</c> for all other reload paths
    /// (filter changes, semester switches, etc.), so those paths never trigger
    /// the animation.
    /// </para>
    /// </summary>
    public string? PendingSavedId { get; private set; }

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
    /// Reloads sections and fires <see cref="SectionsChanged"/> with
    /// <see cref="PendingSavedId"/> set to <paramref name="savedSectionId"/>, so
    /// subscribers can distinguish a save-triggered reload from other reloads and
    /// start the apply-save flash animation on the affected tiles.
    /// <para>
    /// <see cref="PendingSavedId"/> is cleared to <c>null</c> immediately after all
    /// subscribers have returned from <see cref="SectionsChanged"/>, so it is only
    /// readable during the event invocation.
    /// </para>
    /// </summary>
    /// <param name="sectionRepo">Repository for this call.</param>
    /// <param name="semesterIds">Semester IDs to load.</param>
    /// <param name="savedSectionId">The ID of the section that was just saved.</param>
    public void ReloadAfterSave(ISectionRepository sectionRepo,
                                IEnumerable<string> semesterIds,
                                string savedSectionId)
    {
        PendingSavedId = savedSectionId;
        Reload(sectionRepo, semesterIds);
        PendingSavedId = null;
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
    /// Sets <see cref="SelectedSectionId"/> and fires <see cref="SelectionChanged"/>.
    /// Does nothing if <paramref name="sectionId"/> is equal to the current selection,
    /// which prevents echo-back loops between subscribers.
    /// </summary>
    /// <param name="sectionId">The new selection, or <c>null</c> to deselect.</param>
    public void SetSelection(string? sectionId)
    {
        if (SelectedSectionId == sectionId) return;
        SelectedSectionId = sectionId;
        SelectionChanged?.Invoke(sectionId);
    }
}
