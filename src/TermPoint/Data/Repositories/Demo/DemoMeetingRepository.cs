using TermPoint.Demo;
using TermPoint.Models;

namespace TermPoint.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IMeetingRepository"/>.
/// Seeded from <see cref="DemoData.Meetings"/>. All CRUD operations update the
/// in-memory list. Changes are lost on page reload.
/// </summary>
public class DemoMeetingRepository : IMeetingRepository
{
    private readonly List<Meeting> _meetings = [.. DemoData.Meetings];

    /// <inheritdoc/>
    public List<Meeting> GetAll(string semesterId) =>
        [.. _meetings.Where(m => m.SemesterId == semesterId)];

    /// <inheritdoc/>
    public Meeting? GetById(string id) =>
        _meetings.FirstOrDefault(m => m.Id == id);

    /// <inheritdoc/>
    public void Insert(Meeting meeting) => _meetings.Add(meeting);

    /// <inheritdoc/>
    public void Update(Meeting meeting)
    {
        int i = _meetings.FindIndex(m => m.Id == meeting.Id);
        if (i >= 0) _meetings[i] = meeting;
    }

    /// <inheritdoc/>
    public void Delete(string id) => _meetings.RemoveAll(m => m.Id == id);

    /// <inheritdoc/>
    public void DeleteBySemesterId(string semesterId) =>
        _meetings.RemoveAll(m => m.SemesterId == semesterId);

    /// <inheritdoc/>
    public int CountBySemesterId(string semesterId) =>
        _meetings.Count(m => m.SemesterId == semesterId);
}
