using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Room> Rooms =
    [
        new()
        {
            Id         = "demo-room-1",
            Building   = "",
            RoomNumber = "A2111"
        },
        new()
        {
            Id         = "demo-room-2",
            Building   = "",
            RoomNumber = "A328"
        },
        new()
        {
            Id         = "demo-room-3",
            Building   = "",
            RoomNumber = "A331"
        },
        new()
        {
            Id         = "demo-room-4",
            Building   = "",
            RoomNumber = "A337"
        }
    ];
}
