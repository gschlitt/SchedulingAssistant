using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="ILegalStartTimeRepository"/>
/// backed by <see cref="DemoData.LegalStartTimes"/>. Write operations are no-ops.
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
    public void Insert(LegalStartTime entry, string academicYearId) { }

    /// <inheritdoc/>
    public void Update(LegalStartTime entry, string academicYearId) { }

    /// <inheritdoc/>
    public void Delete(string academicYearId, double blockLength) { }

    /// <inheritdoc/>
    public void CopyFromPreviousYear(string toAcademicYearId, string? fromAcademicYearId) { }
}
