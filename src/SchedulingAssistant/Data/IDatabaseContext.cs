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

    /// <summary>
    /// Signals that a user-initiated write is about to occur. On the first call per session
    /// this triggers the dirty marker so crash recovery detection works correctly.
    /// Subsequent calls are no-ops. Ignored in read-only and demo contexts.
    /// </summary>
    void MarkDirty();

    /// <summary>
    /// Resets the dirty state so the next call to <see cref="MarkDirty"/> will fire the
    /// callback again. Called by <see cref="Services.CheckoutService"/> after a successful
    /// save, so that the dirty marker is re-written only when new edits follow.
    /// </summary>
    void ResetDirty();
}
