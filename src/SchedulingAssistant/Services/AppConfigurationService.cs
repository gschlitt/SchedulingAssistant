using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Singleton service that caches DB-persisted application configuration values.
/// Settings stored here travel with the database file (as opposed to machine-level
/// settings in <see cref="AppSettings"/>).
/// </summary>
public class AppConfigurationService
{
    private const string KeyUseSectionPrefixes  = "UseSectionPrefixes";
    private const string KeyGlobalDesignatorType = "GlobalDesignatorType";

    private readonly IAppConfigurationRepository _repo;

    /// <summary>
    /// Whether the institution uses section prefixes (e.g. "AB", "TUT").
    /// When false, section codes are bare designators (e.g. "1", "A").
    /// Defaults to <c>true</c> for new databases.
    /// </summary>
    public bool UseSectionPrefixes { get; private set; }

    /// <summary>
    /// Designator style used when <see cref="UseSectionPrefixes"/> is false.
    /// Governs auto-generation of bare section codes.
    /// Defaults to <see cref="DesignatorType.Number"/>.
    /// </summary>
    public DesignatorType GlobalDesignatorType { get; private set; }

    /// <param name="repo">Repository for the AppConfiguration key-value table.</param>
    public AppConfigurationService(IAppConfigurationRepository repo)
    {
        _repo = repo;
        Load();
    }

    /// <summary>
    /// Reads configuration values from the database into the in-memory cache.
    /// </summary>
    private void Load()
    {
        var usePrefixesRaw = _repo.Get(KeyUseSectionPrefixes);
        UseSectionPrefixes = usePrefixesRaw is null
            ? true                                         // default: on for new DBs
            : usePrefixesRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        var designatorRaw = _repo.Get(KeyGlobalDesignatorType);
        GlobalDesignatorType = designatorRaw?.Equals("Letter", StringComparison.OrdinalIgnoreCase) == true
            ? DesignatorType.Letter
            : DesignatorType.Number;
    }

    /// <summary>
    /// Persists and caches a new value for <see cref="UseSectionPrefixes"/>.
    /// </summary>
    public void SetUseSectionPrefixes(bool value)
    {
        UseSectionPrefixes = value;
        _repo.Set(KeyUseSectionPrefixes, value ? "true" : "false");
    }

    /// <summary>
    /// Persists and caches a new value for <see cref="GlobalDesignatorType"/>.
    /// </summary>
    public void SetGlobalDesignatorType(DesignatorType value)
    {
        GlobalDesignatorType = value;
        _repo.Set(KeyGlobalDesignatorType, value == DesignatorType.Letter ? "Letter" : "Number");
    }
}
