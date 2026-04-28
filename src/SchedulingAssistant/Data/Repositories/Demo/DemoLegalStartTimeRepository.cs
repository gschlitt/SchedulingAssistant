using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="ILegalStartTimeRepository"/>
/// seeded from <see cref="DemoData.LegalStartTimes"/>. Changes are lost on page reload.
/// </summary>
public class DemoLegalStartTimeRepository : ILegalStartTimeRepository
{
    // LegalStartTime has no AcademicYearId field; store it alongside the entry.
    private readonly List<(string AcademicYearId, LegalStartTime Entry)> _entries =
        [.. DemoData.LegalStartTimes.Select(e => (DemoData.AcademicYear.Id, e))];

    /// <inheritdoc/>
    public List<LegalStartTime> GetAll(string academicYearId) =>
        [.. _entries.Where(x => x.AcademicYearId == academicYearId).Select(x => x.Entry)];

    /// <inheritdoc/>
    public LegalStartTime? GetByBlockLength(string academicYearId, double blockLength) =>
        _entries
            .Where(x => x.AcademicYearId == academicYearId && x.Entry.BlockLength == blockLength)
            .Select(x => x.Entry)
            .FirstOrDefault();

    /// <inheritdoc/>
    public void Insert(LegalStartTime entry, string academicYearId) =>
        _entries.Add((academicYearId, entry));

    /// <inheritdoc/>
    public void Update(LegalStartTime entry, string academicYearId)
    {
        int i = _entries.FindIndex(x =>
            x.AcademicYearId == academicYearId && x.Entry.BlockLength == entry.BlockLength);
        if (i >= 0) _entries[i] = (academicYearId, entry);
    }

    /// <inheritdoc/>
    public void Delete(string academicYearId, double blockLength) =>
        _entries.RemoveAll(x =>
            x.AcademicYearId == academicYearId && x.Entry.BlockLength == blockLength);

    /// <inheritdoc/>
    public void CopyFromPreviousYear(string toAcademicYearId, string? fromAcademicYearId)
    {
        if (fromAcademicYearId is null) return;
        var source = _entries
            .Where(x => x.AcademicYearId == fromAcademicYearId)
            .Select(x => (toAcademicYearId, new LegalStartTime
            {
                BlockLength = x.Entry.BlockLength,
                StartTimes  = [.. x.Entry.StartTimes]
            }))
            .ToList();
        _entries.AddRange(source);
    }
}
