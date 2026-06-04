using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="SchedulingNote"/> entities — a single free-text
/// note per (instructor, semester) pair.
/// </summary>
public interface ISchedulingNoteRepository
{
    /// <summary>
    /// Returns the note for the given instructor and semester, or <c>null</c> if none has
    /// been saved yet.
    /// </summary>
    /// <param name="semesterId">The semester to filter by.</param>
    /// <param name="instructorId">The instructor to filter by.</param>
    SchedulingNote? Get(string semesterId, string instructorId);

    /// <summary>Returns all saved notes for the given semester (one per instructor that has one).</summary>
    /// <param name="semesterId">The semester to filter by.</param>
    List<SchedulingNote> GetBySemester(string semesterId);

    /// <summary>
    /// Persists the note for its (instructor, semester) pair, inserting a new row if none
    /// exists or updating the existing row in place. Saving empty text clears the note.
    /// </summary>
    void Save(SchedulingNote note);
}
