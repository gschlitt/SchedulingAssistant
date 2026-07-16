using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Lightweight ID→entity dictionaries used by the enriched shared schedule exporter
/// to resolve section property IDs to display names for the CSV columns.
/// Constructed by <c>SharingViewModel</c> from repository <c>GetAll()</c> calls.
/// </summary>
public record ExportLookups(
    IReadOnlyDictionary<string, Instructor> InstructorsById,
    IReadOnlyDictionary<string, Room> RoomsById,
    IReadOnlyDictionary<string, Campus> CampusesById,
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> SectionTypesById,
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> TagsById,
    IReadOnlyDictionary<string, SchedulingEnvironmentValue> MeetingTypesById);
