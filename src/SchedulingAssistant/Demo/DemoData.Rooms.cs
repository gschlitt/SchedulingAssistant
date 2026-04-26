using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Room> Rooms =
    [
        new()
        {
            Id         = "demo-room-1",
            Building   = "Main",
            RoomNumber = "101"
        },
        new()
        {
            Id         = "demo-room-2",
            Building   = "Main",
            RoomNumber = "102"
        }
    ];
}
