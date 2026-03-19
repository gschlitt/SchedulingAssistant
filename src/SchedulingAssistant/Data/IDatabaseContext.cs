using System.Data.Common;

namespace SchedulingAssistant.Data;

/// <summary>
/// Abstraction over the application database connection.
/// The SQLite desktop implementation opens a file-based database; alternative implementations
/// (e.g., in-memory demo data for WASM) can provide their own version without any connection at all.
/// </summary>
public interface IDatabaseContext : IDisposable
{
    /// <summary>
    /// Gets the open database connection for this context.
    /// Used by all SQL-backed repository implementations to create commands and transactions.
    /// The concrete type is <see cref="Microsoft.Data.Sqlite.SqliteConnection"/> in the desktop build.
    /// </summary>
    DbConnection Connection { get; }
}
