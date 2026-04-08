namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="IAppConfigurationRepository"/>.
/// Reads and writes rows in the <c>AppConfiguration</c> key-value table.
/// </summary>
public class AppConfigurationRepository : IAppConfigurationRepository
{
    private readonly IDatabaseContext _db;

    /// <param name="db">Injected database context providing the open connection.</param>
    public AppConfigurationRepository(IDatabaseContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public string? Get(string key)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM AppConfiguration WHERE key = $key";
        cmd.AddParam("$key", key);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }

    /// <inheritdoc/>
    public void Set(string key, string value)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ($key, $value)";
        cmd.AddParam("$key", key);
        cmd.AddParam("$value", value);
        cmd.ExecuteNonQuery();
    }
}
