using TermPoint.Models;
using System.Text.Json;

namespace TermPoint.Data.Repositories;

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
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Semesters (id, academic_year_id, academic_year_name, name, sort_order, data) " +
            "VALUES ($id, $ayid, (SELECT name FROM AcademicYears WHERE id = $ayid), $name, $so, $data)";
        cmd.AddParam("$id", semester.Id);
        cmd.AddParam("$ayid", semester.AcademicYearId);
        cmd.AddParam("$name", semester.Name);
        cmd.AddParam("$so", semester.SortOrder);
        cmd.AddParam("$data", SerializeData(semester));
        cmd.ExecuteNonQuery();
    }

    public void Update(Semester semester)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE Semesters " +
            "SET academic_year_id = $ayid, " +
                "academic_year_name = (SELECT name FROM AcademicYears WHERE id = $ayid), " +
                "name = $name, sort_order = $so, data = $data " +
            "WHERE id = $id";
        cmd.AddParam("$id", semester.Id);
        cmd.AddParam("$ayid", semester.AcademicYearId);
        cmd.AddParam("$name", semester.Name);
        cmd.AddParam("$so", semester.SortOrder);
        cmd.AddParam("$data", SerializeData(semester));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Semesters WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteByAcademicYear(string academicYearId)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Semesters WHERE academic_year_id = $ayid";
        cmd.AddParam("$ayid", academicYearId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Serializes the extra JSON data for a semester (currently just Color).
    /// The key structural fields (id, name, sort_order, academic_year_id) are stored
    /// in dedicated columns; only supplemental fields go into the data column.
    /// <para>
    /// Any unknown fields found in the data column when the semester was loaded
    /// (<see cref="PersistedRecord.ExtensionData"/>) are written back alongside Color, so an older
    /// app version does not delete fields a newer one added (spec item 3). The payload keeps its
    /// historical format: default System.Text.Json options, hence <c>"Color"</c> not <c>"color"</c>.
    /// </para>
    /// </summary>
    /// <param name="semester">The semester being saved.</param>
    /// <returns>The JSON for the <c>data</c> column.</returns>
    private static string SerializeData(Semester semester)
    {
        var payload = new SemesterData { Color = semester.Color }.WithExtensionDataFrom(semester);
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

            // Deserialize Color (and any unknown fields) from the data column (column 4).
            // Existing rows that have '{}' will deserialize fine — Color stays empty string and
            // there is nothing to preserve.
            if (!reader.IsDBNull(4))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<SemesterData>(reader.GetString(4));
                    if (data is not null)
                    {
                        sem.Color = data.Color ?? string.Empty;
                        // Park fields this version does not know so SerializeData writes them back.
                        sem.CopyExtensionDataFrom(data);
                    }
                }
                catch { /* malformed data — leave Color as default */ }
            }

            results.Add(sem);
        }
        return results;
    }
}
