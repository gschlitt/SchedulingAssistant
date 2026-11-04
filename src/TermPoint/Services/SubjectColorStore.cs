using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// Owns the two subject-colour settings in <c>settings.json</c> (<see cref="AppSettings.SubjectColors"/> and
/// <see cref="AppSettings.SubjectColorDisplay"/>); nothing else reads or writes them. A singleton.
/// <para>
/// <b>Keys.</b> An abbreviation is normalised (trimmed, upper-cased) before use, so <c>bio</c>, <c>Bio </c> and
/// <c>BIO</c> are one entry. Reading also tolerates a hand-edited stored key that is not normalised; writing removes
/// every variant of the key and stores the one normalised entry.
/// </para>
/// <para>
/// <b>Values.</b> Read as normalised upper-case <c>#RRGGBB</c> (an <c>#AARRGGBB</c> value loses its alpha). A stored
/// value that does not parse reads as "no colour" and is left in the dictionary untouched unless that key is
/// explicitly set or cleared.
/// </para>
/// <para>
/// <b>Changes.</b> Each change updates memory, calls <see cref="AppSettings.Save"/> once (a failed save is logged by
/// <c>Save</c> and the colours stay in memory for the session, like every other setting), raises
/// <see cref="AppSettings.SettingChanged"/>, then raises <see cref="Changed"/>. A call that changes nothing saves
/// nothing and raises nothing. No Avalonia types: the palette arrives as hex strings.
/// </para>
/// </summary>
public class SubjectColorStore
{
    /// <summary>Returns the settings object to read and write; re-evaluated on every use (Load replaces the cache).</summary>
    private readonly Func<AppSettings> _settings;

    /// <summary>The settings instance in force right now.</summary>
    private AppSettings Settings => _settings();

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="palette">
    /// The ordered palette as hex strings (<c>#RRGGBB</c>). Entries that do not parse are ignored; the rest are
    /// normalised.
    /// </param>
    /// <param name="settings">
    /// Supplies the settings instance. Defaults to <c>() =&gt; AppSettings.Current</c>, re-read on each call because
    /// <see cref="AppSettings.Load"/> replaces the cached instance. Tests pass their own instance.
    /// </param>
    public SubjectColorStore(IReadOnlyList<string> palette, Func<AppSettings>? settings = null)
    {
        Palette = palette.Select(ColorMath.NormalizeHex).Where(h => h is not null).Select(h => h!).ToList();
        _settings = settings ?? (() => AppSettings.Current);
    }

    /// <summary>The ordered palette, normalised to <c>#RRGGBB</c>, unparseable entries dropped.</summary>
    public IReadOnlyList<string> Palette { get; }

    /// <summary>Raised on the calling (UI) thread after any change has been applied and saved.</summary>
    public event Action? Changed;

    /// <summary>
    /// How colours are shown. An undefined stored number (for example 7 in the file) reads as
    /// <see cref="SubjectColorDisplay.OnlyWhenSeveral"/>. The setter saves and raises events only when the value
    /// changes.
    /// </summary>
    public SubjectColorDisplay Display
    {
        get
        {
            var stored = Settings.SubjectColorDisplay;
            return Enum.IsDefined(stored) ? stored : SubjectColorDisplay.OnlyWhenSeveral;
        }
        set
        {
            if (Display == value) return;
            Settings.SubjectColorDisplay = value;
            Commit(nameof(AppSettings.SubjectColorDisplay));
        }
    }

    /// <summary>
    /// The colour for an abbreviation.
    /// </summary>
    /// <param name="abbreviation">The subject abbreviation (any case or spacing).</param>
    /// <returns>Normalised <c>#RRGGBB</c>, or null if there is none or the stored value does not parse.</returns>
    public string? GetColor(string abbreviation)
    {
        var key = NormalizeKey(abbreviation);
        return key.Length == 0 ? null : ReadColor(Settings.SubjectColors, key);
    }

    /// <summary>
    /// Sets, or (null) removes, one colour. Saves and raises the change events if anything changed.
    /// </summary>
    /// <param name="abbreviation">The subject abbreviation (any case or spacing).</param>
    /// <param name="hex">A colour in <c>#RRGGBB</c> or <c>#AARRGGBB</c> form (alpha dropped), or null to remove.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="abbreviation"/> is empty or whitespace, or <paramref name="hex"/> is non-null and does not parse.
    /// </exception>
    public void SetColor(string abbreviation, string? hex)
    {
        var key = NormalizeKey(abbreviation);
        if (key.Length == 0)
            throw new ArgumentException("The abbreviation must not be empty.", nameof(abbreviation));

        string? normalized = null;
        if (hex is not null)
        {
            normalized = ColorMath.NormalizeHex(hex)
                ?? throw new ArgumentException($"Not a colour in #RRGGBB form: '{hex}'.", nameof(hex));
        }

        if (Apply(Settings.SubjectColors, key, normalized))
            Commit(nameof(AppSettings.SubjectColors));
    }

    /// <summary>
    /// Rename support (spec P2-3, Phase 2 rule 7): copies the old abbreviation's colour to the new one, replacing any
    /// colour the new one had, and keeps the old entry. If the old abbreviation has no valid colour, or both are the
    /// same key (ignoring case and spaces), nothing changes and nothing is saved.
    /// </summary>
    /// <param name="oldAbbreviation">The abbreviation being renamed.</param>
    /// <param name="newAbbreviation">The new abbreviation.</param>
    public void CopyColor(string oldAbbreviation, string newAbbreviation)
    {
        var oldKey = NormalizeKey(oldAbbreviation);
        var newKey = NormalizeKey(newAbbreviation);
        if (oldKey.Length == 0 || newKey.Length == 0 || oldKey == newKey) return;

        var colors = Settings.SubjectColors;
        var color = ReadColor(colors, oldKey);
        if (color is null) return;

        if (Apply(colors, newKey, color))
            Commit(nameof(AppSettings.SubjectColors));
    }

    /// <summary>
    /// Removes the colours of exactly the given abbreviations (this database's subjects); others are untouched. One
    /// save, and the events only if something was removed.
    /// </summary>
    /// <param name="abbreviations">The abbreviations to clear (any case or spacing).</param>
    public void ClearColors(IEnumerable<string> abbreviations)
    {
        var colors = Settings.SubjectColors;
        var changed = false;
        foreach (var key in DistinctKeys(abbreviations))
            changed |= Apply(colors, key, null);
        if (changed) Commit(nameof(AppSettings.SubjectColors));
    }

    /// <summary>
    /// Gives a colour to each of the given abbreviations that has none, using
    /// <see cref="SubjectColorSuggester"/> with the colours of the given abbreviations that do have one as "in use".
    /// Never overwrites a valid colour (a stored value that does not parse counts as "none" and is replaced). One
    /// save, and the events only if something was assigned.
    /// </summary>
    /// <param name="abbreviations">This database's subject abbreviations (any case or spacing).</param>
    public void ApplySuggestions(IEnumerable<string> abbreviations)
    {
        var colors = Settings.SubjectColors;
        var keys = DistinctKeys(abbreviations).ToList();

        var inUse = new List<string>();
        var blanks = new List<string>();
        foreach (var key in keys)
        {
            var c = ReadColor(colors, key);
            if (c is null) blanks.Add(key); else inUse.Add(c);
        }

        var changed = false;
        foreach (var (key, hex) in SubjectColorSuggester.Suggest(blanks, inUse, Palette))
            changed |= Apply(colors, key, hex);
        if (changed) Commit(nameof(AppSettings.SubjectColors));
    }

    /// <summary>
    /// Whether any of the given abbreviations has a valid colour (shows the display choice; enables Clear).
    /// </summary>
    /// <param name="abbreviations">This database's subject abbreviations.</param>
    /// <returns>True if at least one has a colour.</returns>
    public bool HasAnyColor(IEnumerable<string> abbreviations)
    {
        var colors = Settings.SubjectColors;
        return DistinctKeys(abbreviations).Any(k => ReadColor(colors, k) is not null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The canonical key form: trimmed and upper-cased (invariant). Null reads as empty.</summary>
    private static string NormalizeKey(string? abbreviation) => abbreviation?.Trim().ToUpperInvariant() ?? "";

    /// <summary>Normalised, non-empty, distinct keys of the given abbreviations.</summary>
    private static IEnumerable<string> DistinctKeys(IEnumerable<string> abbreviations) =>
        abbreviations.Select(NormalizeKey).Where(k => k.Length > 0).Distinct();

    /// <summary>
    /// The stored keys that mean <paramref name="key"/>: the normalised key itself (first, if present) and any
    /// hand-edited variant that matches after trimming and ignoring case.
    /// </summary>
    private static List<string> MatchingKeys(Dictionary<string, string> colors, string key)
    {
        var matches = new List<string>();
        if (colors.ContainsKey(key)) matches.Add(key);
        foreach (var k in colors.Keys)
            if (k != key && NormalizeKey(k) == key) matches.Add(k);
        return matches;
    }

    /// <summary>
    /// Reads the colour for a normalised key: the first matching stored key whose value parses, as normalised hex.
    /// Never modifies the dictionary.
    /// </summary>
    private static string? ReadColor(Dictionary<string, string> colors, string key)
    {
        foreach (var k in MatchingKeys(colors, key))
        {
            var hex = ColorMath.NormalizeHex(colors[k]);
            if (hex is not null) return hex;
        }
        return null;
    }

    /// <summary>
    /// Writes (or, with null, removes) one entry in memory, removing every non-normalised variant of the key.
    /// </summary>
    /// <param name="colors">The dictionary to change.</param>
    /// <param name="key">The normalised key.</param>
    /// <param name="normalizedHex">A normalised <c>#RRGGBB</c>, or null to remove.</param>
    /// <returns>True if the dictionary's content changed.</returns>
    private static bool Apply(Dictionary<string, string> colors, string key, string? normalizedHex)
    {
        var matches = MatchingKeys(colors, key);

        if (normalizedHex is null)
        {
            foreach (var k in matches) colors.Remove(k);
            return matches.Count > 0;
        }

        // Already exactly one normalised entry with this value: nothing to do.
        if (matches.Count == 1 && matches[0] == key && colors[key] == normalizedHex) return false;

        foreach (var k in matches) colors.Remove(k);
        colors[key] = normalizedHex;
        return true;
    }

    /// <summary>Saves once, then raises <see cref="AppSettings.SettingChanged"/> and <see cref="Changed"/>.</summary>
    /// <param name="settingName">The changed <see cref="AppSettings"/> property name.</param>
    private void Commit(string settingName)
    {
        Settings.Save(); // false = failed; Save has logged it and the change stays in memory for this session.
        AppSettings.RaiseSettingChanged(settingName);
        Changed?.Invoke();
    }
}
