namespace SchedulingAssistant.Models;

public class Room
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Building { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string Features { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Optional foreign key to <c>Campuses.id</c>.
    /// Null when the room is not associated with a specific campus.
    /// </summary>
    public string? CampusId { get; set; }

    /// <summary>
    /// Display order within the room list. Lower values appear first.
    /// Defaults to 0 for all existing records; densely re-packed after each move operation.
    /// </summary>
    public int SortOrder { get; set; } = 0;
}
