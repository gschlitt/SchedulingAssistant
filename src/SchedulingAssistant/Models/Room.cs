namespace SchedulingAssistant.Models;

public class Room
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Building { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string Features { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
