namespace TermPoint.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IAccessChecksSetupRepository"/>, a single row of <c>AppConfiguration</c>
/// (already a readable key/value table, so no new columns or tables).
/// </summary>
public class AccessChecksSetupRepository(IDatabaseContext db) : IAccessChecksSetupRepository
{
    /// <inheritdoc/>
    public bool IsSetUp()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM AppConfiguration WHERE key = $k";
        cmd.AddParam("$k", IAccessChecksSetupRepository.ConfigKey);
        return (cmd.ExecuteScalar() as string)?.Trim() == IAccessChecksSetupRepository.SetUpValue;
    }

    /// <inheritdoc/>
    public void MarkSetUp()
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppConfiguration (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = $v
            """;
        cmd.AddParam("$k", IAccessChecksSetupRepository.ConfigKey);
        cmd.AddParam("$v", IAccessChecksSetupRepository.SetUpValue);
        cmd.ExecuteNonQuery();
    }
}
