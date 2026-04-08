namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// Data access contract for the <c>AppConfiguration</c> key-value table.
/// Stores DB-level settings that travel with the database file (as opposed to
/// machine-level settings stored in <c>AppSettings.json</c>).
/// </summary>
public interface IAppConfigurationRepository
{
    /// <summary>
    /// Returns the stored value for <paramref name="key"/>, or <c>null</c> if the key
    /// is absent.
    /// </summary>
    string? Get(string key);

    /// <summary>
    /// Inserts or replaces the value for <paramref name="key"/>.
    /// </summary>
    void Set(string key, string value);
}
