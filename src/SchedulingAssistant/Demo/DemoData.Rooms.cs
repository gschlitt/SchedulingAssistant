using SchedulingAssistant.Models;

namespace SchedulingAssistant.Demo;

public static partial class DemoData
{
    public static readonly List<Room> Rooms =
    [
        new()
        {
            Id          = "demo-room-1",
            Building    = "Laurens",
            RoomNumber  = "A2111",
            Capacity    = 24,
            CampusId    = "demo-campus-1",
            RoomTypeId  = "demo-rt-4"
        },
        new()
        {
            Id          = "demo-room-2",
            Building    = "Laurens",
            RoomNumber  = "A328",
            Capacity    = 36,
            CampusId    = "demo-campus-1",
            RoomTypeId  = "demo-rt-1"
        },
        new()
        {
            Id          = "demo-room-3",
            Building    = "Roberts",
            RoomNumber  = "A331",
            Capacity    = 24,
            CampusId    = "demo-campus-2",
            RoomTypeId  = "demo-rt-3"
        },
        new()
        {
            Id          = "demo-room-4",
            Building    = "Roberts",
            RoomNumber  = "A337",
            Capacity    = 36,
            CampusId    = "demo-campus-2",
            RoomTypeId  = "demo-rt-1"
        }
    ];
}
