using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IMeetingRepository"/>.
/// No meeting data in the demo. Write operations are no-ops.
/// </summary>
public class DemoMeetingRepository : IMeetingRepository
{
    /// <inheritdoc/>
    public List<Meeting> GetAll(string semesterId) => [];

    /// <inheritdoc/>
    public Meeting? GetById(string id) => null;

    /// <inheritdoc/>
    public void Insert(Meeting meeting) { }

    /// <inheritdoc/>
    public void Update(Meeting meeting) { }

    /// <inheritdoc/>
    public void Delete(string id) { }

    /// <inheritdoc/>
    public void DeleteBySemesterId(string semesterId) { }

    /// <inheritdoc/>
    public int CountBySemesterId(string semesterId) => 0;
}
