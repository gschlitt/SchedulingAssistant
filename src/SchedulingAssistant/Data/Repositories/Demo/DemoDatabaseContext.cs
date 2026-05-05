using System.Data.Common;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// No-op <see cref="IDatabaseContext"/> for the WASM demo build.
/// Demo repositories serve static data and never access the connection.
/// </summary>
public class DemoDatabaseContext : IDatabaseContext
{
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public DbConnection Connection =>
        throw new NotSupportedException("No database connection is available in the demo build.");

    /// <inheritdoc/>
    public bool SupportsTransactions => false;

    /// <inheritdoc/>
    public void MarkDirty() { }

    /// <inheritdoc/>
    public void ResetDirty() { }

    /// <inheritdoc/>
    public void Dispose() { }
}
