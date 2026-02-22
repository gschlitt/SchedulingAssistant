using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class RoomRepository(DatabaseContext db)
{
    public List<Room> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Rooms ORDER BY data ->> 'building', data ->> 'roomNumber'";
        using var reader = cmd.ExecuteReader();
        var results = new List<Room>();
        while (reader.Read())
        {
            var room = JsonHelpers.Deserialize<Room>(reader.GetString(1));
            room.Id = reader.GetString(0);
            results.Add(room);
        }
        return results;
    }

    public Room? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Rooms WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var room = JsonHelpers.Deserialize<Room>(reader.GetString(1));
        room.Id = reader.GetString(0);
        return room;
    }

    public void Insert(Room room)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Rooms (id, data) VALUES ($id, $data)";
        cmd.Parameters.AddWithValue("$id", room.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(room));
        cmd.ExecuteNonQuery();
    }

    public void Update(Room room)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Rooms SET data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", room.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(room));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Rooms WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
