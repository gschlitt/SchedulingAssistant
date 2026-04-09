using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class AcademicYearRepository(IDatabaseContext db) : IAcademicYearRepository
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
        cmd.AddParam("$id", id);
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
        cmd.AddParam("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(AcademicYear academicYear)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO AcademicYears (id, name, data) VALUES ($id, $name, $data)";
        cmd.AddParam("$id", academicYear.Id);
        cmd.AddParam("$name", academicYear.Name);
        cmd.AddParam("$data", JsonHelpers.Serialize(academicYear));
        cmd.ExecuteNonQuery();
    }

    public void Update(AcademicYear academicYear)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE AcademicYears SET name = $name, data = $data WHERE id = $id";
        cmd.AddParam("$id", academicYear.Id);
        cmd.AddParam("$name", academicYear.Name);
        cmd.AddParam("$data", JsonHelpers.Serialize(academicYear));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AcademicYears WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }
}
