using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.Data.Repositories;

public class InstructorRepository(IDatabaseContext db) : IInstructorRepository
{
    /// <summary>
    /// Returns all instructors, ordered according to the persisted
    /// <see cref="AppSettings.InstructorSortMode"/> preference.
    /// <para>
    /// <b>LastName / FirstName / Initials</b> — sort is applied at the SQL level via
    /// <c>ORDER BY</c> on dedicated columns, so the same order is returned to every
    /// caller (section-editor picker, grid-filter list, etc.).
    /// </para>
    /// <para>
    /// <b>StaffType</b> — SQLite does not have access to the resolved staff-type name
    /// (it is a <c>[JsonIgnore]</c> display-only field populated after the query).
    /// The SQL query therefore returns rows in last-name order, and the caller
    /// (<see cref="ViewModels.Management.InstructorListViewModel"/>) re-sorts in memory
    /// by <c>StaffTypeName</c> once name resolution is complete.  Other consumers
    /// (section editor, grid filter) receive last-name order for StaffType mode, which
    /// is still a reasonable fallback.
    /// </para>
    /// </summary>
    public List<Instructor> GetAll()
    {
        // For StaffType the SQL sort is a best-effort fallback; the VM re-sorts in memory
        // after resolving display names (see InstructorListViewModel.Load()).
        var orderBy = AppSettings.Current.InstructorSortMode switch
        {
            InstructorSortMode.FirstName => "first_name, last_name",
            InstructorSortMode.Initials  => "initials, last_name, first_name",
            _                            => "last_name, first_name",   // LastName + StaffType fallback
        };

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = $"SELECT id, data FROM Instructors ORDER BY {orderBy}";
        using var reader = cmd.ExecuteReader();
        var results = new List<Instructor>();
        while (reader.Read())
        {
            var instructor = JsonHelpers.Deserialize<Instructor>(reader.GetString(1));
            instructor.Id = reader.GetString(0);
            results.Add(instructor);
        }
        return results;
    }

    public Instructor? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Instructors WHERE id = $id";
        cmd.AddParam("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var instructor = JsonHelpers.Deserialize<Instructor>(reader.GetString(1));
        instructor.Id = reader.GetString(0);
        return instructor;
    }

    /// <summary>Returns true if any sections reference this instructor (searches JSON instructorAssignments array).</summary>
    public bool HasSections(string instructorId)
    {
        using var cmd = db.Connection.CreateCommand();
        // instructorAssignments is a JSON array of objects; each object has an instructorId property
        cmd.CommandText = """
            SELECT COUNT(*) FROM Sections
            WHERE EXISTS (
                SELECT 1 FROM json_each(data, '$.instructorAssignments')
                WHERE json_extract(value, '$.instructorId') = $id
            )
            """;
        cmd.AddParam("$id", instructorId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Returns true if an instructor with these initials already exists (case-insensitive).
    /// Pass excludeId to ignore the instructor currently being edited.
    /// </summary>
    public bool ExistsByInitials(string initials, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM Instructors WHERE LOWER(data ->> 'initials') = LOWER($initials)"
            : "SELECT COUNT(*) FROM Instructors WHERE LOWER(data ->> 'initials') = LOWER($initials) AND id != $excludeId";
        cmd.AddParam("$initials", initials);
        if (excludeId is not null) cmd.AddParam("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(Instructor instructor)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Instructors (id, last_name, first_name, initials, data) " +
            "VALUES ($id, $lastName, $firstName, $initials, $data)";
        cmd.AddParam("$id", instructor.Id);
        cmd.AddParam("$lastName",  instructor.LastName);
        cmd.AddParam("$firstName", instructor.FirstName);
        cmd.AddParam("$initials",  instructor.Initials);
        cmd.AddParam("$data", JsonHelpers.Serialize(instructor));
        cmd.ExecuteNonQuery();
    }

    public void Update(Instructor instructor, System.Data.Common.DbTransaction? tx = null)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "UPDATE Instructors SET last_name = $lastName, first_name = $firstName, " +
            "initials = $initials, data = $data WHERE id = $id";
        cmd.AddParam("$id", instructor.Id);
        cmd.AddParam("$lastName",  instructor.LastName);
        cmd.AddParam("$firstName", instructor.FirstName);
        cmd.AddParam("$initials",  instructor.Initials);
        cmd.AddParam("$data", JsonHelpers.Serialize(instructor));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Instructors WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }
}
