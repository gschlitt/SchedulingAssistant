namespace TermPoint.Models;

/// <summary>
/// One semester (e.g. "Fall") within an academic year; each semester is a separate schedule.
/// <para>
/// A semester is <b>not</b> serialized directly: its identity fields live in dedicated columns and
/// only <see cref="Color"/> goes into the <c>data</c> column, via <see cref="SemesterData"/>. It still
/// derives from <see cref="PersistedRecord"/> because its inherited
/// <see cref="PersistedRecord.ExtensionData"/> holds the unknown fields of that <c>data</c> column,
/// loaded by <c>SemesterRepository</c> and written back by it. Code that rebuilds a semester
/// (e.g. the Rename copy) must carry them across (see <see cref="PersistedRecord"/>).
/// </para>
/// </summary>
public class Semester : PersistedRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AcademicYearId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>
    /// Hex color string (e.g. "#C65D1E") assigned to this semester for grid display.
    /// Empty string falls back to the name-based color lookup in ScheduleGridViewModel.
    /// </summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// The fixed set of semester names auto-created for each Academic Year.
    /// </summary>
    public static readonly string[] DefaultNames =
        ["Fall", "Winter", "Early Summer", "Summer", "Late Summer"];
}
