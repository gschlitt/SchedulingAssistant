using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ISchedulingNoteRepository"/>.
/// Starts empty; notes added during a demo session live only until the page reloads.
/// </summary>
public class DemoSchedulingNoteRepository : ISchedulingNoteRepository
{
    private readonly List<SchedulingNote> _notes = [];

    /// <inheritdoc/>
    public SchedulingNote? Get(string semesterId, string instructorId) =>
        _notes.FirstOrDefault(n => n.SemesterId == semesterId && n.InstructorId == instructorId);

    /// <inheritdoc/>
    public List<SchedulingNote> GetBySemester(string semesterId) =>
        [.. _notes.Where(n => n.SemesterId == semesterId)];

    /// <inheritdoc/>
    public void Save(SchedulingNote note)
    {
        var existing = Get(note.SemesterId, note.InstructorId);
        if (existing is null)
            _notes.Add(note);
        else
            existing.Text = note.Text;
    }
}
