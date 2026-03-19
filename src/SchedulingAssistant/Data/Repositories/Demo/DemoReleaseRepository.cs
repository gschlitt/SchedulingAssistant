using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IReleaseRepository"/>.
/// No release data is included in the demo snapshot, so all query methods
/// return empty/null results.  Write operations are no-ops.
/// </summary>
public class DemoReleaseRepository : IReleaseRepository
{
    /// <inheritdoc/>
    public List<Release> GetBySemester(string semesterId) => [];

    /// <inheritdoc/>
    public List<Release> GetByInstructor(string semesterId, string instructorId) => [];

    /// <inheritdoc/>
    public Release? GetById(string id) => null;

    /// <inheritdoc/>
    public void Insert(Release release) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(Release release) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string id) { /* no-op in demo */ }
}
