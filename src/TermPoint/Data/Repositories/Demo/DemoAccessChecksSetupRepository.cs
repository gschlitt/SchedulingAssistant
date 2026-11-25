namespace TermPoint.Data.Repositories.Demo;

/// <summary>
/// In-memory demo implementation of <see cref="IAccessChecksSetupRepository"/>. Starts "not set up"; the mark
/// lives only until the page reloads.
/// </summary>
public class DemoAccessChecksSetupRepository : IAccessChecksSetupRepository
{
    private bool _isSetUp;

    /// <inheritdoc/>
    public bool IsSetUp() => _isSetUp;

    /// <inheritdoc/>
    public void MarkSetUp(System.Data.Common.DbTransaction? tx = null) => _isSetUp = true;
}
