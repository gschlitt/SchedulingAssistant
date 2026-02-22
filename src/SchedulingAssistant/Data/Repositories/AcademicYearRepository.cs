using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class AcademicYearRepository(DatabaseContext db)
{
    public List<AcademicYear> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM AcademicYears ORDER BY CAST(SUBSTR(data ->> 'name', 1, 4) AS INTEGER) DESC";
        using var reader = cmd.ExecuteReader();
        var results = new List<AcademicYear>();
        while (reader.Read())
        {
            var ay = JsonHelpers.Deserialize<AcademicYear>(reader.GetString(1));
            ay.Id = reader.GetString(0);
            results.Add(ay);
        }
        return results;
    }

    public AcademicYear? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM AcademicYears WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var ay = JsonHelpers.Deserialize<AcademicYear>(reader.GetString(1));
        ay.Id = reader.GetString(0);
        return ay;
    }

    public bool ExistsByName(string name)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AcademicYears WHERE data ->> 'name' = $name";
        cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(AcademicYear academicYear)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO AcademicYears (id, data) VALUES ($id, $data)";
        cmd.Parameters.AddWithValue("$id", academicYear.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(academicYear));
        cmd.ExecuteNonQuery();
    }

    public void Update(AcademicYear academicYear)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE AcademicYears SET data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", academicYear.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(academicYear));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AcademicYears WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
