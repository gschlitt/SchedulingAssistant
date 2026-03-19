using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ILegalStartTimeRepository"/> backed by
/// <see cref="DemoData.LegalStartTimes"/>.  All entries belong to the single demo
/// academic year; queries for any other year return empty results.  Write operations are no-ops.
/// </summary>
public class DemoLegalStartTimeRepository : ILegalStartTimeRepository
{
    /// <inheritdoc/>
    public List<LegalStartTime> GetAll(string academicYearId) =>
        academicYearId == DemoData.AcademicYear.Id
            ? [.. DemoData.LegalStartTimes]
            : [];

    /// <inheritdoc/>
    public LegalStartTime? GetByBlockLength(string academicYearId, double blockLength) =>
        academicYearId == DemoData.AcademicYear.Id
            ? DemoData.LegalStartTimes.FirstOrDefault(e => e.BlockLength == blockLength)
            : null;

    /// <inheritdoc/>
    public void Insert(LegalStartTime entry, string academicYearId) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Update(LegalStartTime entry, string academicYearId) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void Delete(string academicYearId, double blockLength) { /* no-op in demo */ }

    /// <inheritdoc/>
    public void CopyFromPreviousYear(string toAcademicYearId, string? fromAcademicYearId) { /* no-op in demo */ }
}
