using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="LegalStartTime"/> entities.
/// Each entry defines the allowed section start times for a given block length within an academic year.
/// </summary>
public interface ILegalStartTimeRepository
{
    /// <summary>
    /// Returns all legal start time entries for the given academic year.
    /// </summary>
    /// <param name="academicYearId">The academic year to retrieve start times for.</param>
    List<LegalStartTime> GetAll(string academicYearId);

    /// <summary>
    /// Returns the entry for a specific <paramref name="blockLength"/> within an academic year,
    /// or <c>null</c> if no entry exists.
    /// </summary>
    /// <param name="academicYearId">The academic year to look up.</param>
    /// <param name="blockLength">The block length in hours (e.g. 1.5, 2.0, 3.0).</param>
    LegalStartTime? GetByBlockLength(string academicYearId, double blockLength);

    /// <summary>
    /// Inserts a new legal start time entry for the given academic year.
    /// The composite key is (<paramref name="academicYearId"/>, <see cref="LegalStartTime.BlockLength"/>).
    /// </summary>
    void Insert(LegalStartTime entry, string academicYearId);

    /// <summary>
    /// Updates the entry identified by (<paramref name="academicYearId"/>, <see cref="LegalStartTime.BlockLength"/>).
    /// </summary>
    void Update(LegalStartTime entry, string academicYearId);

    /// <summary>
    /// Deletes the entry for the given academic year and block length.
    /// </summary>
    void Delete(string academicYearId, double blockLength);

    /// <summary>
    /// Copies all legal start time entries from <paramref name="fromAcademicYearId"/> into
    /// <paramref name="toAcademicYearId"/>. If <paramref name="fromAcademicYearId"/> is <c>null</c>,
    /// no copy is performed.
    /// </summary>
    void CopyFromPreviousYear(string toAcademicYearId, string? fromAcademicYearId);
}
