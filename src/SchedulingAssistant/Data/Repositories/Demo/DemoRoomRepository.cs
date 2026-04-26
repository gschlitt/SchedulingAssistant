using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// Read-only demo implementation of <see cref="IRoomRepository"/>
/// backed by <see cref="DemoData.Rooms"/>. Write operations are no-ops.
/// </summary>
public class DemoRoomRepository : IRoomRepository
{
    /// <inheritdoc/>
    public List<Room> GetAll() =>
        [.. DemoData.Rooms.OrderBy(r => r.Building).ThenBy(r => r.RoomNumber)];

    /// <inheritdoc/>
    public Room? GetById(string id) =>
        DemoData.Rooms.FirstOrDefault(r => r.Id == id);

    /// <inheritdoc/>
    public void Insert(Room room) { }

    /// <inheritdoc/>
    public void Update(Room room) { }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null) { }
}
