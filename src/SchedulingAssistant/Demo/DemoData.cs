using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

/// <summary>
/// Static repository of hard-coded demo data for the WASM browser build.
/// Populated by the Demo Data Generator (Debug flyout) from a real SQLite database.
/// Each entity type lives in its own partial-class file.
/// </summary>
public static partial class DemoData
{
    /// <summary>
    /// All scheduling environment values across all types, for lookup by ID.
    /// </summary>
    public static IEnumerable<SchedulingEnvironmentValue> AllSchedulingEnvironmentValues =>
        SectionTypes
            .Concat(MeetingTypes)
            .Concat(StaffTypes)
            .Concat(Tags)
            .Concat(Resources)
            .Concat(Reserves);
}
