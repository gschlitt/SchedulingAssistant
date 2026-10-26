using Microsoft.Data.Sqlite;
using Xunit;
using TermPoint.Data;
using TermPoint.Data.Repositories;
using TermPoint.Models;
using TermPoint.Services.SectionImport;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// A temporary SQLite database with an academic year (2026-2027) and a semester (Fall), the real
/// repositories over it, and the section import's builder, writer and service wired to them. Disposing it
/// closes the database and deletes the folder (database, <c>-wal</c> and <c>-shm</c> files included).
/// Individual repositories can be replaced with decorators for the writer, to watch or break its calls.
/// </summary>
public sealed class ImportRig : IDisposable
{
    private readonly bool _deleteFolder;

    /// <summary>The temporary folder holding the database.</summary>
    public string Folder { get; }

    /// <summary>The database file.</summary>
    public string DbPath { get; }

    /// <summary>The open database context.</summary>
    public DatabaseContext Db { get; }

    /// <summary>The academic year seeded into the database.</summary>
    public AcademicYear Year { get; }

    /// <summary>The open semester seeded into the database.</summary>
    public Semester Fall { get; }

    /// <summary>The real subject repository.</summary>
    public SubjectRepository Subjects { get; }

    /// <summary>The real campus repository.</summary>
    public CampusRepository Campuses { get; }

    /// <summary>The real repository of section types, meeting types and tags.</summary>
    public SchedulingEnvironmentRepository Environment { get; }

    /// <summary>The real room repository.</summary>
    public RoomRepository Rooms { get; }

    /// <summary>The real course repository.</summary>
    public CourseRepository Courses { get; }

    /// <summary>The real instructor repository.</summary>
    public InstructorRepository Instructors { get; }

    /// <summary>The real section repository.</summary>
    public SectionRepository Sections { get; }

    /// <summary>Builds snapshots from the real repositories.</summary>
    public ImportSetupSnapshotBuilder SnapshotBuilder { get; }

    /// <summary>The writer over the real repositories.</summary>
    public SectionImportWriter Writer { get; }

    /// <summary>The service over the real repositories.</summary>
    public SectionImportService Service { get; }

    /// <summary>
    /// Opens the database in the given folder and wires everything. When <paramref name="seed"/> is true the
    /// academic year and semester are inserted; pass false to open a database that already has them.
    /// </summary>
    /// <param name="folder">The folder to use; null creates a new temporary one.</param>
    /// <param name="onFirstWrite">The context's first-write callback (the dirty marker hook).</param>
    /// <param name="readOnly">Open the context read-only.</param>
    /// <param name="seed">Insert the academic year and semester.</param>
    /// <param name="year">The existing academic year when <paramref name="seed"/> is false.</param>
    /// <param name="semester">The existing semester when <paramref name="seed"/> is false.</param>
    /// <param name="deleteFolder">Delete the folder on dispose; false for a second rig opened on another rig's folder.</param>
    public ImportRig(string? folder = null, Action? onFirstWrite = null, bool readOnly = false,
        bool seed = true, AcademicYear? year = null, Semester? semester = null, bool deleteFolder = true)
    {
        _deleteFolder = deleteFolder;
        Folder = folder ?? Path.Combine(Path.GetTempPath(), $"import_rig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Folder);
        DbPath = Path.Combine(Folder, "test.db");
        Db = new DatabaseContext(DbPath, onFirstWrite, readOnly);

        Subjects = new SubjectRepository(Db);
        Campuses = new CampusRepository(Db);
        Environment = new SchedulingEnvironmentRepository(Db);
        Rooms = new RoomRepository(Db);
        Courses = new CourseRepository(Db);
        Instructors = new InstructorRepository(Db);
        Sections = new SectionRepository(Db);

        if (seed)
        {
            Year = new AcademicYear { Name = SectionImportTestKit.AcademicYearName };
            new AcademicYearRepository(Db).Insert(Year);
            Fall = new Semester { AcademicYearId = Year.Id, Name = SectionImportTestKit.TermName };
            new SemesterRepository(Db).Insert(Fall);
        }
        else
        {
            Year = year!;
            Fall = semester!;
        }

        SnapshotBuilder = new ImportSetupSnapshotBuilder(
            new SemesterRepository(Db), new AcademicYearRepository(Db), Subjects, Courses, Instructors, Sections,
            Environment, Campuses, Rooms, new LegalStartTimeRepository(Db));
        Writer = NewWriter();
        Service = new SectionImportService(new ImportFileReader(), SnapshotBuilder, Writer);
    }

    /// <summary>
    /// Builds a writer over this database, with any of the seven repositories replaced by a decorator.
    /// </summary>
    /// <param name="subjects">Replacement subject repository, or null for the real one.</param>
    /// <param name="campuses">Replacement campus repository, or null.</param>
    /// <param name="environment">Replacement setup-value repository, or null.</param>
    /// <param name="rooms">Replacement room repository, or null.</param>
    /// <param name="courses">Replacement course repository, or null.</param>
    /// <param name="instructors">Replacement instructor repository, or null.</param>
    /// <param name="sections">Replacement section repository, or null.</param>
    public SectionImportWriter NewWriter(
        ISubjectRepository? subjects = null, ICampusRepository? campuses = null,
        ISchedulingEnvironmentRepository? environment = null, IRoomRepository? rooms = null,
        ICourseRepository? courses = null, IInstructorRepository? instructors = null,
        ISectionRepository? sections = null) =>
        new(Db, subjects ?? Subjects, campuses ?? Campuses, environment ?? Environment, rooms ?? Rooms,
            courses ?? Courses, instructors ?? Instructors, sections ?? Sections);

    /// <summary>Plans a sheet against the database as it is now.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The report.</returns>
    public ImportReport Plan(ImportSheet sheet) =>
        SectionImportPlanner.Plan(sheet, SnapshotBuilder.Build(Fall.Id));

    /// <summary>
    /// Every row of every table, each as one text line of all its column values, keyed by table name.
    /// Used to prove nothing existing changed and nothing was added.
    /// </summary>
    /// <returns>Table name to its rows (in rowid order).</returns>
    public Dictionary<string, List<string>> DumpAllTables()
    {
        var result = new Dictionary<string, List<string>>();
        var tables = new List<string>();
        using (var cmd = Db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            var rows = new List<string>();
            using var cmd = Db.Connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < values.Length; i++)
                    values[i] = reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i)) ?? "";
                rows.Add(string.Join("\u001F", values));
            }
            result[table] = rows;
        }
        return result;
    }

    /// <summary>
    /// For every table that has both an <c>id</c> and a <c>data</c> column, every row's id mapped to its
    /// <c>data</c> text. Used to prove that no existing record was changed by an import (compare a snapshot
    /// taken before with one taken after: every id of the first must be in the second with identical text),
    /// which is a sharper statement than a row count.
    /// </summary>
    /// <returns>Table name to (id to data).</returns>
    public Dictionary<string, Dictionary<string, string>> SnapshotIdToData()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        var tables = new List<string>();
        using (var cmd = Db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = Db.Connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("id") || !columns.Contains("data"))
                continue;

            var rows = new Dictionary<string, string>();
            using var select = Db.Connection.CreateCommand();
            select.CommandText = $"SELECT id, data FROM \"{table}\"";
            using var rows2 = select.ExecuteReader();
            while (rows2.Read())
                rows[rows2.GetString(0)] = rows2.IsDBNull(1) ? "<null>" : rows2.GetString(1);
            result[table] = rows;
        }
        return result;
    }

    /// <summary>
    /// Compares two <see cref="SnapshotIdToData"/> snapshots and describes how the second differs from the
    /// first, treating anything other than an insert as a violation: a row that vanished, or a row whose
    /// <c>data</c> text changed.
    /// </summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <param name="after">The later snapshot.</param>
    /// <param name="inserted">How many rows were added, per table (tables with none are left out).</param>
    /// <returns>One line per violation; empty when the second snapshot is the first plus inserts.</returns>
    public static List<string> FindNonInserts(
        Dictionary<string, Dictionary<string, string>> before,
        Dictionary<string, Dictionary<string, string>> after,
        out Dictionary<string, int> inserted)
    {
        var violations = new List<string>();
        inserted = new Dictionary<string, int>();
        foreach (var (table, oldRows) in before)
        {
            if (!after.TryGetValue(table, out var newRows))
            {
                violations.Add($"{table}: table missing after");
                continue;
            }
            foreach (var (id, data) in oldRows)
            {
                if (!newRows.TryGetValue(id, out var newData))
                    violations.Add($"{table}: row {id} was removed");
                else if (!string.Equals(data, newData, StringComparison.Ordinal))
                    violations.Add($"{table}: row {id} data changed");
            }
            int added = newRows.Keys.Count(id => !oldRows.ContainsKey(id));
            if (added > 0)
                inserted[table] = added;
        }
        return violations;
    }

    /// <summary>
    /// Runs a query that returns one row and reads its columns as text.
    /// </summary>
    /// <param name="sql">The query; must return exactly one row.</param>
    /// <param name="parameters">(name, value) parameters.</param>
    /// <returns>The row's values as text (null for SQL NULL).</returns>
    public string?[] QueryRow(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Db.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.AddParam(name, value);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read(), $"No row returned by: {sql}");
        var values = new string?[reader.FieldCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
        Assert.False(reader.Read(), $"More than one row returned by: {sql}");
        return values;
    }

    /// <summary>Closes the database, releases pooled connections and deletes the folder.</summary>
    public void Dispose()
    {
        Db.Dispose();
        SqliteConnection.ClearAllPools();
        if (_deleteFolder)
        {
            try { Directory.Delete(Folder, recursive: true); } catch { /* best effort */ }
        }
    }
}
