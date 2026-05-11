using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Room> Rooms =
    [
        new()
        {
            Id         = "demo-room-1",
            Building   = "Main Building",
            RoomNumber = "A2111",
            Capacity   = 40,
            CampusId   = "demo-campus-2"
        },
        new()
        {
            Id         = "demo-room-2",
            Building   = "Main Building",
            RoomNumber = "A328",
            Capacity   = 40,
            CampusId   = "demo-campus-2"
        },
        new()
        {
            Id         = "demo-room-3",
            Building   = "Main Building",
            RoomNumber = "A331",
            Capacity   = 45,
            CampusId   = "demo-campus-2"
        },
        new()
        {
            Id         = "demo-room-4",
            Building   = "Main Building",
            RoomNumber = "A337",
            Capacity   = 45,
            CampusId   = "demo-campus-2"
        }
    ];
}
