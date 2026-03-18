using System.Data.Common;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for <see cref="Room"/> entities.
/// </summary>
public interface IRoomRepository
{
    /// <summary>Returns all rooms, ordered by building then room number.</summary>
    List<Room> GetAll();

    /// <summary>Returns the room with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Room? GetById(string id);

    /// <summary>Inserts a new room. The <see cref="Room.Id"/> must already be set.</summary>
    void Insert(Room room);

    /// <summary>Updates the room matched by <see cref="Room.Id"/>.</summary>
    void Update(Room room);

    /// <summary>
    /// Deletes the room with the given <paramref name="id"/>.
    /// An optional <paramref name="tx"/> may be supplied to include this write in a larger transaction.
    /// </summary>
    void Delete(string id, DbTransaction? tx = null);
}
