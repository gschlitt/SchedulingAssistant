using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IReleaseRepository"/>.
/// Starts empty; all CRUD operations update the in-memory list. Changes are lost on
/// page reload.
/// </summary>
public class DemoReleaseRepository : IReleaseRepository
{
    private readonly List<Release> _releases = [];

    /// <inheritdoc/>
    public List<Release> GetBySemester(string semesterId) =>
        [.. _releases.Where(r => r.SemesterId == semesterId)];

    /// <inheritdoc/>
    public List<Release> GetByInstructor(string semesterId, string instructorId) =>
        [.. _releases.Where(r => r.SemesterId == semesterId && r.InstructorId == instructorId)];

    /// <inheritdoc/>
    public Release? GetById(string id) =>
        _releases.FirstOrDefault(r => r.Id == id);

    /// <inheritdoc/>
    public void Insert(Release release) => _releases.Add(release);

    /// <inheritdoc/>
    public void Update(Release release)
    {
        int i = _releases.FindIndex(r => r.Id == release.Id);
        if (i >= 0) _releases[i] = release;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _releases.RemoveAll(r => r.Id == id);
}
