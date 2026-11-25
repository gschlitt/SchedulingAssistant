namespace TermPoint.Data.Repositories;

/// <summary>
/// Reads and writes the Access Analyzer's "set-up" mark: the <c>AppConfiguration</c> row
/// <see cref="ConfigKey"/> with value <c>1</c>, written when the user finishes the access-checks wizard
/// (either answer). Until it exists the analyzer uses its suggestion and invites the setup.
/// </summary>
public interface IAccessChecksSetupRepository
{
    /// <summary>The <c>AppConfiguration.key</c> holding the mark.</summary>
    const string ConfigKey = "AccessChecksSetUp";

    /// <summary>The value stored under <see cref="ConfigKey"/> once the setup is done.</summary>
    const string SetUpValue = "1";

    /// <summary>Whether the access-checks setup has been finished. An absent row means "not set up".</summary>
    /// <returns>True when the mark is present with value <c>1</c>.</returns>
    bool IsSetUp();

    /// <summary>
    /// Records that the setup is finished. Safe to repeat. A read-only session is refused like any other write.
    /// </summary>
    /// <param name="tx">Optional transaction to join; null for auto-commit behaviour.</param>
    /// <exception cref="InvalidOperationException">Not thrown by the demo version. The SQLite version throws <c>WriteAccessLostException</c>;The session is read-only; nothing was written.</exception>
    void MarkSetUp(System.Data.Common.DbTransaction? tx = null);
}
