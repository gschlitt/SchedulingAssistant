using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class SemesterRepository(DatabaseContext db)
{
    public List<Semester> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, academic_year_id, name, sort_order FROM Semesters ORDER BY sort_order, name";
        return ReadSemesters(cmd);
    }

    public List<Semester> GetByAcademicYear(string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, academic_year_id, name, sort_order FROM Semesters WHERE academic_year_id = $ayid ORDER BY sort_order";
        cmd.Parameters.AddWithValue("$ayid", academicYearId);
        return ReadSemesters(cmd);
    }

    public Semester? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, academic_year_id, name, sort_order FROM Semesters WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadSemesters(cmd).FirstOrDefault();
    }

    public void Insert(Semester semester)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Semesters (id, academic_year_id, name, sort_order, data) VALUES ($id, $ayid, $name, $so, '{}')";
        cmd.Parameters.AddWithValue("$id", semester.Id);
        cmd.Parameters.AddWithValue("$ayid", semester.AcademicYearId);
        cmd.Parameters.AddWithValue("$name", semester.Name);
        cmd.Parameters.AddWithValue("$so", semester.SortOrder);
        cmd.ExecuteNonQuery();
    }

    public void Update(Semester semester)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE Semesters SET academic_year_id = $ayid, name = $name, sort_order = $so WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", semester.Id);
        cmd.Parameters.AddWithValue("$ayid", semester.AcademicYearId);
        cmd.Parameters.AddWithValue("$name", semester.Name);
        cmd.Parameters.AddWithValue("$so", semester.SortOrder);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Semesters WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteByAcademicYear(string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Semesters WHERE academic_year_id = $ayid";
        cmd.Parameters.AddWithValue("$ayid", academicYearId);
        cmd.ExecuteNonQuery();
    }

    private static List<Semester> ReadSemesters(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<Semester>();
        while (reader.Read())
            results.Add(new Semester
            {
                Id = reader.GetString(0),
                AcademicYearId = reader.GetString(1),
                Name = reader.GetString(2),
                SortOrder = reader.GetInt32(3)
            });
        return results;
    }
}
