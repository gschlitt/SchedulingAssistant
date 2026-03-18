using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class RoomRepository(IDatabaseContext db) : IRoomRepository
{
    /// <summary>
    /// Returns all rooms ordered by <see cref="Room.SortOrder"/> ascending, then by building
    /// and room number as a tiebreaker.  Sorting is done in C# after deserialization so that
    /// existing rows whose JSON predates the SortOrder field correctly default to 0.
    /// </summary>
    public List<Room> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Rooms";
        using var reader = cmd.ExecuteReader();
        var results = new List<Room>();
        while (reader.Read())
        {
            var room = JsonHelpers.Deserialize<Room>(reader.GetString(1));
            room.Id = reader.GetString(0);
            results.Add(room);
        }
        return results
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Building,    StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RoomNumber,  StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Room? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Rooms WHERE id = $id";
        cmd.AddParam("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var room = JsonHelpers.Deserialize<Room>(reader.GetString(1));
        room.Id = reader.GetString(0);
        return room;
    }

    public void Insert(Room room)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Rooms (id, building, room_number, data) VALUES ($id, $building, $roomNumber, $data)";
        cmd.AddParam("$id", room.Id);
        cmd.AddParam("$building",   room.Building);
        cmd.AddParam("$roomNumber", room.RoomNumber);
        cmd.AddParam("$data", JsonHelpers.Serialize(room));
        cmd.ExecuteNonQuery();
    }

    public void Update(Room room)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE Rooms SET building = $building, room_number = $roomNumber, data = $data WHERE id = $id";
        cmd.AddParam("$id", room.Id);
        cmd.AddParam("$building",   room.Building);
        cmd.AddParam("$roomNumber", room.RoomNumber);
        cmd.AddParam("$data", JsonHelpers.Serialize(room));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id, System.Data.Common.DbTransaction? tx = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM Rooms WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }
}
