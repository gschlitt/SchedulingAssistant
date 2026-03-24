using SchedulingAssistant.Models;
using System.Text.Json;

namespace SchedulingAssistant.Data.Repositories;

public class SemesterRepository(IDatabaseContext db) : ISemesterRepository
{
    public List<Semester> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, academic_year_id, name, sort_order, data FROM Semesters ORDER BY sort_order, name";
        return ReadSemesters(cmd);
    }

    public List<Semester> GetByAcademicYear(string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, academic_year_id, name, sort_order, data FROM Semesters WHERE academic_year_id = $ayid ORDER BY sort_order";
        cmd.AddParam("$ayid", academicYearId);
        return ReadSemesters(cmd);
    }

    public Semester? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, academic_year_id, name, sort_order, data FROM Semesters WHERE id = $id";
        cmd.AddParam("$id", id);
        return ReadSemesters(cmd).FirstOrDefault();
    }

    public void Insert(Semester semester)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Semesters (id, academic_year_id, name, sort_order, data) VALUES ($id, $ayid, $name, $so, $data)";
        cmd.AddParam("$id", semester.Id);
        cmd.AddParam("$ayid", semester.AcademicYearId);
        cmd.AddParam("$name", semester.Name);
        cmd.AddParam("$so", semester.SortOrder);
        cmd.AddParam("$data", SerializeData(semester));
        cmd.ExecuteNonQuery();
    }

    public void Update(Semester semester)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE Semesters SET academic_year_id = $ayid, name = $name, sort_order = $so, data = $data WHERE id = $id";
        cmd.AddParam("$id", semester.Id);
        cmd.AddParam("$ayid", semester.AcademicYearId);
        cmd.AddParam("$name", semester.Name);
        cmd.AddParam("$so", semester.SortOrder);
        cmd.AddParam("$data", SerializeData(semester));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Semesters WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteByAcademicYear(string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Semesters WHERE academic_year_id = $ayid";
        cmd.AddParam("$ayid", academicYearId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Serializes the extra JSON data for a semester (currently just Color).
    /// The key structural fields (id, name, sort_order, academic_year_id) are stored
    /// in dedicated columns; only supplemental fields go into the data column.
    /// </summary>
    private static string SerializeData(Semester semester)
    {
        var payload = new { semester.Color };
        return JsonSerializer.Serialize(payload);
    }

    private static List<Semester> ReadSemesters(System.Data.Common.DbCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<Semester>();
        while (reader.Read())
        {
            var sem = new Semester
            {
                Id             = reader.GetString(0),
                AcademicYearId = reader.GetString(1),
                Name           = reader.GetString(2),
                SortOrder      = reader.GetInt32(3),
            };

            // Deserialize Color from the data column (column 4).
            // Existing rows that have '{}' will deserialize fine — Color stays empty string.
            if (!reader.IsDBNull(4))
            {
                try
                {
                    var json = reader.GetString(4);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Color", out var colorEl))
                        sem.Color = colorEl.GetString() ?? string.Empty;
                }
                catch { /* malformed data — leave Color as default */ }
            }

            results.Add(sem);
        }
        return results;
    }
}
