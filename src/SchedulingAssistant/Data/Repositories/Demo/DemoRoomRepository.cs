using System.Data.Common;
using SchedulingAssistant.Demo;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IRoomRepository"/> backed by a
/// mutable copy of <see cref="DemoData.Rooms"/>. All CRUD operations update the
/// in-memory list; changes are lost on page reload.
/// </summary>
public class DemoRoomRepository : IRoomRepository
{
    private readonly List<Room> _rooms = [.. DemoData.Rooms];

    /// <inheritdoc/>
    public List<Room> GetAll() =>
        [.. _rooms.OrderBy(r => r.Building).ThenBy(r => r.RoomNumber)];

    /// <inheritdoc/>
    public Room? GetById(string id) =>
        _rooms.FirstOrDefault(r => r.Id == id);

    /// <inheritdoc/>
    public void Insert(Room room) => _rooms.Add(room);

    /// <inheritdoc/>
    public void Update(Room room)
    {
        int i = _rooms.FindIndex(r => r.Id == room.Id);
        if (i >= 0) _rooms[i] = room;
    }

    /// <inheritdoc/>
    public void Delete(string id, DbTransaction? tx = null) =>
        _rooms.RemoveAll(r => r.Id == id);
}
