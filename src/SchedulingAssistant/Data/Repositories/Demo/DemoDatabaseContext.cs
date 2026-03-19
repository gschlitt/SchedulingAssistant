using System.Data.Common;
using SchedulingAssistant.Data;

namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// No-op implementation of <see cref="IDatabaseContext"/> for the WASM demo build.
/// Demo repositories serve static data and never access the connection, so this stub
/// exists solely to satisfy DI registrations that require an <see cref="IDatabaseContext"/>.
/// Accessing <see cref="Connection"/> throws <see cref="NotSupportedException"/>.
/// </summary>
public class DemoDatabaseContext : IDatabaseContext
{
    /// <summary>
    /// Not supported in the demo build — demo repositories do not execute SQL.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public DbConnection Connection =>
        throw new NotSupportedException("No database connection is available in the demo build.");

    /// <inheritdoc/>
    public void Dispose() { /* nothing to dispose */ }
}
