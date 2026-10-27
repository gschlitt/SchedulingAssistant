using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 25: how section cards show room and instructor conflicts, in full (one line per conflict) or as one
/// compact flag per kind with the full list on hover. Covers the per-user setting and its default, the Preferences
/// radio pair that edits it, and the card's property lines in each mode, including the redraw when the setting
/// changes. Rendering the flags (icons, one shared line, the tooltip) is <c>PropertyLinesBehavior</c>'s job and needs a
/// live Avalonia layout, so it is checked by hand. Every test that changes <see cref="AppSettings.Current"/> runs
/// inside a <see cref="SettingsFileGuard"/>, which puts the settings back afterwards.
/// </summary>
public class SectionConflictDisplayTests
{
    private const string RoomConflicts = "Room clash with HIST 101 B\nRoom clash with HIST 205 A";
    private const string InstructorConflicts = "J. Smith also teaches HIST 202 A";

    /// <summary>A card with tags and both kinds of conflict.</summary>
    private static SectionListItemViewModel CardWithBothConflicts()
    {
        var card = SectionCardPropertyLinesTests.MakeCard(tags: true);
        card.RoomConflictWarning = RoomConflicts;
        card.InstructorConflictWarning = InstructorConflicts;
        return card;
    }

    // ── The setting ─────────────────────────────────────────────────────────────

    /// <summary>The default is today's look, conflicts in full.</summary>
    [Fact]
    public void AppSettings_Default_IsFull()
        => Assert.Equal(ConflictDisplayMode.Full, new AppSettings().SectionConflictDisplay);

    /// <summary>A settings file from before this setting existed loads with the default, and the value round-trips.</summary>
    [Fact]
    public void AppSettings_OldFileLoadsAsFull_AndTheValueRoundTrips()
    {
        Assert.Equal(ConflictDisplayMode.Full,
            JsonSerializer.Deserialize<AppSettings>("{\"IncludeSaturday\":true}")!.SectionConflictDisplay);

        var json = JsonSerializer.Serialize(new AppSettings { SectionConflictDisplay = ConflictDisplayMode.Flag });
        Assert.Equal(ConflictDisplayMode.Flag, JsonSerializer.Deserialize<AppSettings>(json)!.SectionConflictDisplay);
    }

    // ── Preferences ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Choosing "As a flag" stores the value, writes it to disk, updates both radio buttons and tells live views once;
    /// choosing it again, or un-checking a button (what the radio group does to the other one), changes nothing.
    /// </summary>
    [Fact]
    public void Preferences_ChoosingFlags_SavesAndNotifiesOnce()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.SectionConflictDisplay = ConflictDisplayMode.Full;
        var vm = new PreferencesViewModel();
        Assert.True(vm.ShowConflictsInFull);
        Assert.False(vm.ShowConflictsAsFlags);

        var settingChanges = new List<string>();
        var propertyChanges = new List<string?>();
        void OnSetting(string name) => settingChanges.Add(name);
        AppSettings.SettingChanged += OnSetting;
        vm.PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
        try
        {
            vm.ShowConflictsAsFlags = true;
            vm.ShowConflictsInFull = false;    // the radio group un-checks the other button: ignored
            vm.ShowConflictsAsFlags = true;    // same value: nothing more
        }
        finally
        {
            AppSettings.SettingChanged -= OnSetting;
        }

        Assert.Equal(ConflictDisplayMode.Flag, AppSettings.Current.SectionConflictDisplay);
        Assert.True(vm.ShowConflictsAsFlags);
        Assert.False(vm.ShowConflictsInFull);
        Assert.Equal(new[] { nameof(AppSettings.SectionConflictDisplay) }, settingChanges);
        Assert.Equal(
            new[] { nameof(PreferencesViewModel.ShowConflictsInFull), nameof(PreferencesViewModel.ShowConflictsAsFlags) },
            propertyChanges);
        Assert.Equal(ConflictDisplayMode.Flag, AppSettings.Load().SectionConflictDisplay);   // really on disk
    }

    // ── The card's lines ────────────────────────────────────────────────────────

    /// <summary>In full, the conflict lines are as before and carry no hover text.</summary>
    [Fact]
    public void Full_ConflictLinesAreTheFullText_WithNoDetail()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.SectionConflictDisplay = ConflictDisplayMode.Full;

        var lines = CardWithBothConflicts().PropertyLines;

        Assert.Equal(
            new[]
            {
                new CardPropertyLine(CardPropertyKind.RoomConflict, RoomConflicts),
                new CardPropertyLine(CardPropertyKind.InstructorConflict, InstructorConflicts),
            },
            lines.Take(2));
        Assert.All(lines, l => Assert.Null(l.Detail));
    }

    /// <summary>
    /// As flags, each kind of conflict becomes one flag with the approved short text and the full list as its hover
    /// text, still ahead of the other lines.
    /// </summary>
    [Fact]
    public void Flag_EachKindIsOneFlag_WithTheFullListAsDetail()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.SectionConflictDisplay = ConflictDisplayMode.Flag;

        var lines = CardWithBothConflicts().PropertyLines;

        Assert.Equal(
            new[]
            {
                new CardPropertyLine(CardPropertyKind.RoomConflictFlag, "Room conflict", RoomConflicts),
                new CardPropertyLine(CardPropertyKind.InstructorConflictFlag, "Instructor conflict", InstructorConflicts),
            },
            lines.Take(2));
        Assert.Equal(CardPropertyKind.Tags, lines[2].Kind);
        Assert.Equal(3, lines.Count);
    }

    /// <summary>As flags, only the kinds that have conflicts get a flag; a blank warning gives none.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Flag_OnlyKindsWithConflictsGetAFlag(bool room, bool instructor)
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.SectionConflictDisplay = ConflictDisplayMode.Flag;
        var card = SectionCardPropertyLinesTests.MakeCard();
        card.RoomConflictWarning = room ? RoomConflicts : "  ";
        card.InstructorConflictWarning = instructor ? InstructorConflicts : null;

        var expected = new List<CardPropertyKind>();
        if (room)       expected.Add(CardPropertyKind.RoomConflictFlag);
        if (instructor) expected.Add(CardPropertyKind.InstructorConflictFlag);

        Assert.Equal(expected, card.PropertyLines.Select(l => l.Kind));
        Assert.Equal(expected.Count > 0, card.HasPropertyLines);
    }

    /// <summary>
    /// After the setting changes, <see cref="SectionListItemViewModel.RefreshPropertyLines"/> tells the card to redraw
    /// (both notifications), and the lines it then reads follow the new setting.
    /// </summary>
    [Fact]
    public void RefreshPropertyLines_NotifiesTheCard_AndTheLinesFollowTheSetting()
    {
        using var guard = new SettingsFileGuard();
        AppSettings.Current.SectionConflictDisplay = ConflictDisplayMode.Full;
        var card = CardWithBothConflicts();
        Assert.Equal(CardPropertyKind.RoomConflict, card.PropertyLines[0].Kind);

        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        AppSettings.Current.SectionConflictDisplay = ConflictDisplayMode.Flag;
        card.RefreshPropertyLines();

        Assert.Equal(
            new[] { nameof(SectionListItemViewModel.PropertyLines), nameof(SectionListItemViewModel.HasPropertyLines) },
            changed);
        Assert.Equal(CardPropertyKind.RoomConflictFlag, card.PropertyLines[0].Kind);
    }
}
