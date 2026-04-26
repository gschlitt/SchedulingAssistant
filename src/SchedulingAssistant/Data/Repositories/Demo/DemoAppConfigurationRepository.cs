namespace SchedulingAssistant.Data.Repositories.Demo;

/// <summary>
/// No-op implementation of <see cref="IAppConfigurationRepository"/> for the WASM demo.
/// Returns null for all keys; writes are silently discarded.
/// </summary>
public class DemoAppConfigurationRepository : IAppConfigurationRepository
{
    /// <inheritdoc/>
    public string? Get(string key) => null;

    /// <inheritdoc/>
    public void Set(string key, string value) { }
}
