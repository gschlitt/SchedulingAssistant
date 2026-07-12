using TermPoint.Models;

namespace TermPoint.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IProgramWatchRepository"/>.
/// Starts empty; watches added during a demo session live only until the page reloads.
/// </summary>
public class DemoProgramWatchRepository : IProgramWatchRepository
{
    private readonly List<(string SemesterId, ProgramWatch Watch)> _watches = [];

    /// <inheritdoc/>
    public List<ProgramWatch> GetAll(string semesterId) =>
        [.. _watches.Where(w => w.SemesterId == semesterId).Select(w => w.Watch)];

    /// <inheritdoc/>
    public void Save(string semesterId, ProgramWatch watch)
    {
        var idx = _watches.FindIndex(w => w.Watch.Id == watch.Id);
        if (idx >= 0)
            _watches[idx] = (semesterId, watch);
        else
            _watches.Add((semesterId, watch));
    }

    /// <inheritdoc/>
    public void Delete(string id)
    {
        _watches.RemoveAll(w => w.Watch.Id == id);
    }
}
