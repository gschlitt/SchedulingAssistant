using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SubjectColorStore"/>. Each test passes its own <see cref="AppSettings"/>; the settings file
/// is redirected to a temp folder for the whole test assembly, so Save is safe.
/// </summary>
public sealed class SubjectColorStoreTests
{
    private static readonly string[] Palette = { "#B7E79D", "#EBB4E6", "#F8C58C", "#F2A89A" };

    /// <summary>Builds a store over a fresh settings instance.</summary>
    private static (SubjectColorStore Store, AppSettings Settings) Make()
    {
        var settings = new AppSettings();
        return (new SubjectColorStore(Palette, () => settings), settings);
    }

    /// <summary>bio, "Bio " and BIO are one entry stored under BIO.</summary>
    [Fact]
    public void Keys_AreNormalised_OneEntry()
    {
        var (store, settings) = Make();
        store.SetColor("bio", "#112233");
        store.SetColor("Bio ", "#445566");
        store.SetColor("BIO", "#778899");
        Assert.Single(settings.SubjectColors);
        Assert.Equal("#778899", settings.SubjectColors["BIO"]);
        Assert.Equal("#778899", store.GetColor(" bIo "));
    }

    /// <summary>A hand-edited non-normalised stored key is still read, and replaced by one entry on write.</summary>
    [Fact]
    public void HandEditedKey_ReadAndConsolidated()
    {
        var (store, settings) = Make();
        settings.SubjectColors["bio "] = "#112233";
        Assert.Equal("#112233", store.GetColor("BIO"));
        store.SetColor("BIO", "#445566");
        Assert.Single(settings.SubjectColors);
        Assert.Equal("#445566", settings.SubjectColors["BIO"]);
    }

    /// <summary>An #AARRGGBB value reads with alpha dropped.</summary>
    [Fact]
    public void AlphaValue_ReadsWithoutAlpha()
    {
        var (store, settings) = Make();
        settings.SubjectColors["BIOL"] = "#80B7E79D";
        Assert.Equal("#B7E79D", store.GetColor("BIOL"));
    }

    /// <summary>SetColor stores the normalised value and null removes it.</summary>
    [Fact]
    public void SetColor_NormalisesAndNullRemoves()
    {
        var (store, settings) = Make();
        store.SetColor("BIOL", "#80b7e79d");
        Assert.Equal("#B7E79D", settings.SubjectColors["BIOL"]);
        store.SetColor("BIOL", null);
        Assert.Empty(settings.SubjectColors);
    }

    /// <summary>Bad hex or empty abbreviation throws ArgumentException.</summary>
    [Fact]
    public void SetColor_BadInput_Throws()
    {
        var (store, settings) = Make();
        Assert.Throws<ArgumentException>(() => store.SetColor("BIOL", "not-a-colour"));
        Assert.Throws<ArgumentException>(() => store.SetColor("  ", "#112233"));
        Assert.Empty(settings.SubjectColors);
    }

    /// <summary>CopyColor keeps the old key and overwrites the new one's colour.</summary>
    [Fact]
    public void CopyColor_KeepsOld_OverwritesNew()
    {
        var (store, settings) = Make();
        store.SetColor("PHYS", "#112233");
        store.SetColor("PHYX", "#445566");
        store.CopyColor("phys", "PHYX");
        Assert.Equal("#112233", store.GetColor("PHYS"));
        Assert.Equal("#112233", store.GetColor("PHYX"));
        Assert.Equal(2, settings.SubjectColors.Count);
    }

    /// <summary>CopyColor from a colourless key changes nothing and raises nothing; same key is a no-op.</summary>
    [Fact]
    public void CopyColor_NoOldColor_NoChange()
    {
        var (store, settings) = Make();
        store.SetColor("PHYX", "#445566");
        var fired = 0;
        store.Changed += () => fired++;
        store.CopyColor("PHYS", "PHYX");
        store.CopyColor("PHYX", " phyx");
        Assert.Equal("#445566", store.GetColor("PHYX"));
        Assert.Single(settings.SubjectColors);
        Assert.Equal(0, fired);
    }

    /// <summary>ClearColors touches only the abbreviations given.</summary>
    [Fact]
    public void ClearColors_OnlyGiven()
    {
        var (store, _) = Make();
        store.SetColor("A", "#111111");
        store.SetColor("B", "#222222");
        store.SetColor("C", "#333333");
        store.ClearColors(new[] { "a", "B", "Z" });
        Assert.Null(store.GetColor("A"));
        Assert.Null(store.GetColor("B"));
        Assert.Equal("#333333", store.GetColor("C"));
    }

    /// <summary>A bad stored value reads as null and stays in the dictionary after another key is set and saved.</summary>
    [Fact]
    public void BadStoredValue_NullButKept()
    {
        var (store, settings) = Make();
        settings.SubjectColors["BAD"] = "not-a-colour";
        Assert.Null(store.GetColor("BAD"));
        store.SetColor("GOOD", "#112233");
        Assert.Equal("not-a-colour", settings.SubjectColors["BAD"]);
        Assert.False(store.HasAnyColor(new[] { "BAD" }));
        Assert.True(store.HasAnyColor(new[] { "BAD", "GOOD" }));
    }

    /// <summary>Missing keys: no colours, Display OnlyWhenSeveral; an undefined stored number reads as the default.</summary>
    [Fact]
    public void Defaults_AndUndefinedDisplayNumber()
    {
        var (store, settings) = Make();
        Assert.Null(store.GetColor("BIOL"));
        Assert.False(store.HasAnyColor(new[] { "BIOL" }));
        Assert.Equal(SubjectColorDisplay.OnlyWhenSeveral, store.Display);
        settings.SubjectColorDisplay = (SubjectColorDisplay)7;
        Assert.Equal(SubjectColorDisplay.OnlyWhenSeveral, store.Display);
    }

    /// <summary>ApplySuggestions fills only colourless ones and never overwrites.</summary>
    [Fact]
    public void ApplySuggestions_NeverOverwrites()
    {
        var (store, _) = Make();
        store.SetColor("BIO", "#B7E79D"); // palette[0], so suggestions must avoid it
        store.ApplySuggestions(new[] { "bio", "CHEM", "ART" });
        Assert.Equal("#B7E79D", store.GetColor("BIO"));
        Assert.Equal("#EBB4E6", store.GetColor("ART"));
        Assert.Equal("#F8C58C", store.GetColor("CHEM"));
        Assert.Null(store.GetColor("OTHER"));
    }

    /// <summary>Changed fires once per change, never on a no-op; the Display setter likewise.</summary>
    [Fact]
    public void Changed_OncePerChange_NotOnNoOp()
    {
        var (store, _) = Make();
        var fired = 0;
        var names = new List<string>();
        void OnSetting(string n) => names.Add(n);
        AppSettings.SettingChanged += OnSetting;
        try
        {
            store.Changed += () => fired++;

            store.SetColor("A", "#111111");                      // 1
            store.SetColor("A", "#111111");                      // no-op
            store.SetColor("B", null);                           // no-op
            store.ApplySuggestions(new[] { "A", "B", "C" });     // 2 (one save for two additions)
            store.ApplySuggestions(new[] { "A", "B", "C" });     // no-op
            store.ClearColors(new[] { "Z" });                    // no-op
            store.ClearColors(new[] { "A", "B" });               // 3
            store.CopyColor("C", "D");                           // 4
            store.Display = SubjectColorDisplay.OnlyWhenSeveral; // no-op (already the default)
            store.Display = SubjectColorDisplay.Always;          // 5
            store.Display = SubjectColorDisplay.Always;          // no-op

            Assert.Equal(5, fired);
            Assert.Equal(5, names.Count);
            Assert.Equal(nameof(AppSettings.SubjectColors), names[0]);
            Assert.Equal(nameof(AppSettings.SubjectColorDisplay), names[^1]);
        }
        finally { AppSettings.SettingChanged -= OnSetting; }
    }

    /// <summary>Unparseable palette entries are ignored and the rest normalised.</summary>
    [Fact]
    public void Palette_IgnoresBadEntries()
    {
        var store = new SubjectColorStore(new[] { "#b7e79d", "junk", "#80EBB4E6" }, () => new AppSettings());
        Assert.Equal(new[] { "#B7E79D", "#EBB4E6" }, store.Palette);
    }
}
