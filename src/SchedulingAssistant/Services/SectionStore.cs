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

    // ── Selection ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The ID of the currently selected section, or <c>null</c> when nothing is selected.
    /// Use <see cref="SetSelection"/> to change this value — do not set it directly.
    /// </summary>
    public string? SelectedSectionId { get; private set; }

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
    public void Reload(SectionRepository sectionRepo, IEnumerable<string> semesterIds)
    {
        var dict = new Dictionary<string, IReadOnlyList<Section>>();
        foreach (var semId in semesterIds)
            dict[semId] = sectionRepo.GetAll(semId).AsReadOnly();

        _sectionsBySemester = dict;
        Sections = dict.Values.SelectMany(v => v).ToList();
        SectionsChanged?.Invoke();
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
