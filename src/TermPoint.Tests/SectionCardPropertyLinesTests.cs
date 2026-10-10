using TermPoint.Models;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SectionListItemViewModel.PropertyLines"/> and
/// <see cref="SectionListItemViewModel.HasPropertyLines"/> — the structured data that the section
/// card's single property-lines <c>TextBlock</c> renders (spec item 21, step 3). They cover which
/// lines appear and in what order, omission of blank text, text passing through verbatim, and the
/// change notifications that make the card rebuild when a conflict warning is applied after
/// construction.
///
/// The attached behavior that turns these lines into inlines (<c>PropertyLinesBehavior</c>) is not
/// tested here: it needs a live Avalonia text layout, and the project has no headless Avalonia
/// test infrastructure. Driven with plain model objects and empty-or-tiny lookup dictionaries —
/// no database or UI.
/// </summary>
public class SectionCardPropertyLinesTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a lookup entry with the given id and display name.</summary>
    private static SchedulingEnvironmentValue Value(string id, string name) => new() { Id = id, Name = name };

    /// <summary>
    /// Builds a card for a section whose tag, reserve and resource lines are present or absent as
    /// requested. A section with none of them has none of the three lines (both conflict warnings
    /// start null, as they do on a freshly built card).
    /// </summary>
    /// <param name="tags">Give the section two tags ("Honours", "Lab").</param>
    /// <param name="reserves">Give the section one reserve, which formats as "Nursing:5".</param>
    /// <param name="resources">Give the section two resources ("Projector", "Whiteboard").</param>
    private static SectionListItemViewModel MakeCard(bool tags = false, bool reserves = false, bool resources = false)
    {
        var section = new Section { CourseId = "c-1", SectionCode = "A" };
        var tagLookup      = new Dictionary<string, SchedulingEnvironmentValue>();
        var reserveLookup  = new Dictionary<string, SchedulingEnvironmentValue>();
        var resourceLookup = new Dictionary<string, SchedulingEnvironmentValue>();

        if (tags)
        {
            section.TagIds.AddRange(["t-1", "t-2"]);
            tagLookup["t-1"] = Value("t-1", "Honours");
            tagLookup["t-2"] = Value("t-2", "Lab");
        }
        if (reserves)
        {
            section.Reserves.Add(new SectionReserve { ReserveId = "r-1", Code = 5 });
            reserveLookup["r-1"] = Value("r-1", "Nursing");
        }
        if (resources)
        {
            section.ResourceIds.AddRange(["x-1", "x-2"]);
            resourceLookup["x-1"] = Value("x-1", "Projector");
            resourceLookup["x-2"] = Value("x-2", "Whiteboard");
        }

        return BuildCard(section, tagLookup, reserveLookup, resourceLookup);
    }

    /// <summary>Constructs a card from a section and the three lookups that feed its property lines.</summary>
    private static SectionListItemViewModel BuildCard(
        Section section,
        Dictionary<string, SchedulingEnvironmentValue> tagLookup,
        Dictionary<string, SchedulingEnvironmentValue> reserveLookup,
        Dictionary<string, SchedulingEnvironmentValue> resourceLookup) =>
        new(
            section,
            courseLookup:       new Dictionary<string, Course>(),
            instructorLookup:   new Dictionary<string, Instructor>(),
            roomLookup:         new Dictionary<string, Room>(),
            sectionTypeLookup:  new Dictionary<string, SchedulingEnvironmentValue>(),
            campusLookup:       new Dictionary<string, Campus>(),
            tagLookup:          tagLookup,
            resourceLookup:     resourceLookup,
            reserveLookup:      reserveLookup,
            meetingTypeLookup:  new Dictionary<string, SchedulingEnvironmentValue>());

    /// <summary>The kinds of a card's lines, in order.</summary>
    private static CardPropertyKind[] Kinds(SectionListItemViewModel card) =>
        card.PropertyLines.Select(l => l.Kind).ToArray();

    /// <summary>Starts recording the names of the properties the card reports as changed.</summary>
    private static List<string?> RecordChanges(SectionListItemViewModel card)
    {
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Order and omission
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllFivePresent_AreInDisplayOrder()
    {
        var card = MakeCard(tags: true, reserves: true, resources: true);
        card.RoomConflictWarning = "Room clash with HIST 101 B";
        card.InstructorConflictWarning = "Smith is also teaching HIST 202 A";

        Assert.Equal(
            new[]
            {
                CardPropertyKind.RoomConflict,
                CardPropertyKind.InstructorConflict,
                CardPropertyKind.Tags,
                CardPropertyKind.Reserves,
                CardPropertyKind.Resources,
            },
            Kinds(card));
        Assert.True(card.HasPropertyLines);
    }

    [Fact]
    public void NonePresent_GivesEmptyList_AndHasPropertyLinesFalse()
    {
        var card = MakeCard();

        Assert.Empty(card.PropertyLines);
        Assert.False(card.HasPropertyLines);
    }

    [Theory]
    // Each row: room conflict, instructor conflict, tags, reserves, resources.
    [InlineData(true,  false, false, false, false)]
    [InlineData(false, true,  false, false, false)]
    [InlineData(false, false, true,  false, false)]
    [InlineData(false, false, false, true,  false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(true,  false, true,  false, false)]
    [InlineData(false, true,  false, true,  true)]
    [InlineData(true,  true,  false, false, true)]
    [InlineData(false, false, true,  true,  true)]
    [InlineData(true,  true,  true,  true,  false)]
    public void AnySubset_GivesOnlyThoseLines_InDisplayOrder(
        bool room, bool instructor, bool tags, bool reserves, bool resources)
    {
        var card = MakeCard(tags, reserves, resources);
        if (room)       card.RoomConflictWarning = "room warning";
        if (instructor) card.InstructorConflictWarning = "instructor warning";

        var expected = new List<CardPropertyKind>();
        if (room)       expected.Add(CardPropertyKind.RoomConflict);
        if (instructor) expected.Add(CardPropertyKind.InstructorConflict);
        if (tags)       expected.Add(CardPropertyKind.Tags);
        if (reserves)   expected.Add(CardPropertyKind.Reserves);
        if (resources)  expected.Add(CardPropertyKind.Resources);

        Assert.Equal(expected, Kinds(card));
        Assert.Equal(expected.Count > 0, card.HasPropertyLines);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Blank text
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    public void BlankConflictWarnings_AreOmitted(string blank)
    {
        var card = MakeCard(tags: true);
        card.RoomConflictWarning = blank;
        card.InstructorConflictWarning = blank;

        Assert.Equal(new[] { CardPropertyKind.Tags }, Kinds(card));
    }

    [Fact]
    public void WhitespaceOnlyTagName_IsOmitted_SoNoBlankLineIsShown()
    {
        // A tag whose name is only spaces yields a whitespace TagLine; it must not produce a line
        // (and so no icon) with nothing to read next to it.
        var section = new Section { CourseId = "c-1", SectionCode = "A" };
        section.TagIds.Add("t-1");
        var card = BuildCard(
            section,
            new Dictionary<string, SchedulingEnvironmentValue> { ["t-1"] = Value("t-1", "   ") },
            new Dictionary<string, SchedulingEnvironmentValue>(),
            new Dictionary<string, SchedulingEnvironmentValue>());

        Assert.Empty(card.PropertyLines);
        Assert.False(card.HasPropertyLines);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Line text
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EachLinesText_EqualsItsSourceProperty()
    {
        var card = MakeCard(tags: true, reserves: true, resources: true);
        card.RoomConflictWarning = "Room clash with HIST 101 B";
        card.InstructorConflictWarning = "Smith is also teaching HIST 202 A";

        var byKind = card.PropertyLines.ToDictionary(l => l.Kind, l => l.Text);

        Assert.Equal(card.RoomConflictWarning,       byKind[CardPropertyKind.RoomConflict]);
        Assert.Equal(card.InstructorConflictWarning, byKind[CardPropertyKind.InstructorConflict]);
        Assert.Equal(card.TagLine,                   byKind[CardPropertyKind.Tags]);
        Assert.Equal(card.ReserveLine,               byKind[CardPropertyKind.Reserves]);
        Assert.Equal(card.ResourceLine,              byKind[CardPropertyKind.Resources]);

        // And the source properties are what the card has always displayed.
        Assert.Equal("Honours, Lab", card.TagLine);
        Assert.Equal("Nursing:5", card.ReserveLine);
        Assert.Equal("Projector, Whiteboard", card.ResourceLine);
    }

    [Fact]
    public void LineText_IsPassedThroughVerbatim_NeverParsedAsMarkup()
    {
        // Tag names are user-entered; characters that look like inline markup must reach the
        // view untouched (this is why the card does not use the markup-parsing InlineFormatter).
        const string name = "**bold** [[pill]] [link](http://example.com) {red}x{/}";
        var section = new Section { CourseId = "c-1", SectionCode = "A" };
        section.TagIds.Add("t-1");
        var card = BuildCard(
            section,
            new Dictionary<string, SchedulingEnvironmentValue> { ["t-1"] = Value("t-1", name) },
            new Dictionary<string, SchedulingEnvironmentValue>(),
            new Dictionary<string, SchedulingEnvironmentValue>());

        var line = Assert.Single(card.PropertyLines);
        Assert.Equal(CardPropertyKind.Tags, line.Kind);
        Assert.Equal(name, line.Text);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Notification — conflict warnings are applied after construction
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(CardPropertyKind.RoomConflict)]
    [InlineData(CardPropertyKind.InstructorConflict)]
    public void SettingAWarning_AfterConstruction_RaisesNotifications_AndAddsTheLine(CardPropertyKind kind)
    {
        var card = MakeCard(tags: true);
        var changed = RecordChanges(card);

        if (kind == CardPropertyKind.RoomConflict)
            card.RoomConflictWarning = "warning text";
        else
            card.InstructorConflictWarning = "warning text";

        Assert.Contains(nameof(SectionListItemViewModel.PropertyLines), changed);
        Assert.Contains(nameof(SectionListItemViewModel.HasPropertyLines), changed);

        // The fresh list includes the warning, ahead of the tags line.
        Assert.Equal(new[] { kind, CardPropertyKind.Tags }, Kinds(card));
        Assert.Equal("warning text", card.PropertyLines[0].Text);
    }

    [Theory]
    [InlineData(CardPropertyKind.RoomConflict)]
    [InlineData(CardPropertyKind.InstructorConflict)]
    public void ClearingAWarning_RaisesNotifications_AndRemovesTheLine(CardPropertyKind kind)
    {
        var card = MakeCard(tags: true);
        if (kind == CardPropertyKind.RoomConflict)
            card.RoomConflictWarning = "warning text";
        else
            card.InstructorConflictWarning = "warning text";
        var changed = RecordChanges(card);

        if (kind == CardPropertyKind.RoomConflict)
            card.RoomConflictWarning = null;
        else
            card.InstructorConflictWarning = null;

        Assert.Contains(nameof(SectionListItemViewModel.PropertyLines), changed);
        Assert.Contains(nameof(SectionListItemViewModel.HasPropertyLines), changed);
        Assert.Equal(new[] { CardPropertyKind.Tags }, Kinds(card));
    }

    [Fact]
    public void WarningOnAnOtherwiseEmptyCard_TurnsHasPropertyLinesOn_ThenOff()
    {
        var card = MakeCard();
        Assert.False(card.HasPropertyLines);

        card.RoomConflictWarning = "warning text";
        Assert.True(card.HasPropertyLines);

        card.RoomConflictWarning = null;
        Assert.False(card.HasPropertyLines);
    }

    [Fact]
    public void EachRead_OfPropertyLines_ReflectsTheCurrentWarnings()
    {
        // PropertyLines is computed per read, so a stale list can never be returned after the
        // warnings change.
        var card = MakeCard();
        var before = card.PropertyLines;

        card.InstructorConflictWarning = "warning text";
        var after = card.PropertyLines;

        Assert.Empty(before);
        Assert.Single(after);
    }
}
