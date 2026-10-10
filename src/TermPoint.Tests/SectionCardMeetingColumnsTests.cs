using TermPoint.Models;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the six meeting-table column strings on <see cref="SectionListItemViewModel"/>
/// (<c>MeetingDayLines</c>, <c>MeetingStartLines</c>, <c>MeetingEndLines</c>,
/// <c>MeetingFrequencyLines</c>, <c>MeetingRoomLines</c>, <c>MeetingTypeLines</c>) that the section
/// card's single-Grid meeting table renders (spec item 21, step 4).
///
/// In the view each string sits directly after its column's header Run inside one TextBlock, so
/// the header is line 1 and each meeting is the next line down. These tests pin the contract that
/// keeps the six columns aligned row by row: every column has exactly one line per meeting, an
/// empty value is a non-breaking space (so its line is as tall as the others), and a newline
/// inside a value can never add a line.
///
/// Driven with plain model objects and small lookup dictionaries — no database or UI. Expected
/// strings for times and frequencies are taken from <see cref="SectionListItemViewModel.MeetingDetails"/>
/// rather than hard-coded, so how a time is formatted ("0830" vs "08:30") stays the view model's
/// existing business and these tests only check how the column strings are assembled.
/// </summary>
public class SectionCardMeetingColumnsTests
{
    /// <summary>A non-breaking space — what an empty value becomes so its line keeps its height.</summary>
    private static readonly string Nbsp = ((char)0x00A0).ToString();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a card for a section with the given meetings.
    /// </summary>
    /// <param name="meetings">The section's schedule entries (the card sorts them by day, then start).</param>
    /// <param name="rooms">Room lookup, keyed by room id; empty when null.</param>
    /// <param name="meetingTypes">Meeting-type lookup, keyed by meeting-type id; empty when null.</param>
    private static SectionListItemViewModel MakeCard(
        IEnumerable<SectionDaySchedule> meetings,
        Dictionary<string, Room>? rooms = null,
        Dictionary<string, SchedulingEnvironmentValue>? meetingTypes = null)
    {
        var section = new Section { CourseId = "c-1", SectionCode = "A" };
        section.Schedule.AddRange(meetings);

        return new SectionListItemViewModel(
            section,
            courseLookup:       new Dictionary<string, Course>(),
            instructorLookup:   new Dictionary<string, Instructor>(),
            roomLookup:         rooms ?? new Dictionary<string, Room>(),
            sectionTypeLookup:  new Dictionary<string, SchedulingEnvironmentValue>(),
            campusLookup:       new Dictionary<string, Campus>(),
            tagLookup:          new Dictionary<string, SchedulingEnvironmentValue>(),
            resourceLookup:     new Dictionary<string, SchedulingEnvironmentValue>(),
            reserveLookup:      new Dictionary<string, SchedulingEnvironmentValue>(),
            meetingTypeLookup:  meetingTypes ?? new Dictionary<string, SchedulingEnvironmentValue>());
    }

    /// <summary>Builds one schedule entry (a 90-minute meeting unless a start/duration is given).</summary>
    private static SectionDaySchedule Meeting(
        int day, int start = 510, int duration = 90,
        string? roomId = null, string? meetingTypeId = null, string? frequency = null) =>
        new()
        {
            Day = day, StartMinutes = start, DurationMinutes = duration,
            RoomId = roomId, MeetingTypeId = meetingTypeId, Frequency = frequency,
        };

    /// <summary>Builds a room entry whose label on the card is "<paramref name="building"/> <paramref name="number"/>".</summary>
    private static Room MakeRoom(string id, string building, string number) =>
        new() { Id = id, Building = building, RoomNumber = number };

    /// <summary>Builds a meeting-type lookup entry.</summary>
    private static SchedulingEnvironmentValue MakeType(string id, string name) => new() { Id = id, Name = name };

    /// <summary>
    /// The six column strings of a card, in the order the view lays the columns out
    /// (Day, Start, End, Freq, Room, Type).
    /// </summary>
    private static string[] Columns(SectionListItemViewModel card) =>
    [
        card.MeetingDayLines,
        card.MeetingStartLines,
        card.MeetingEndLines,
        card.MeetingFrequencyLines,
        card.MeetingRoomLines,
        card.MeetingTypeLines,
    ];

    /// <summary>
    /// The number of lines a column's TextBlock shows: the header Run's line plus the column
    /// string, split the way the text layout splits it.
    /// </summary>
    private static int RenderedLineCount(string columnString) => ("Header" + columnString).Split('\n').Length;

    // ══════════════════════════════════════════════════════════════════════════
    // No meetings
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ZeroMeetings_AllSixStringsAreEmpty()
    {
        var card = MakeCard([]);

        Assert.Empty(card.MeetingDetails);
        Assert.Equal("", card.MeetingDayLines);
        Assert.Equal("", card.MeetingStartLines);
        Assert.Equal("", card.MeetingEndLines);
        Assert.Equal("", card.MeetingFrequencyLines);
        Assert.Equal("", card.MeetingRoomLines);
        Assert.Equal("", card.MeetingTypeLines);
    }

    [Fact]
    public void ZeroMeetings_EachColumnShowsOnlyItsHeaderLine()
    {
        // No trailing line break: the card with no meetings must not gain a blank line.
        var card = MakeCard([]);

        Assert.All(Columns(card), column => Assert.Equal(1, RenderedLineCount(column)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // One and several meetings
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void OneMeeting_EachStringIsALineBreakThenTheValue()
    {
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "Science", "A101") };
        var types = new Dictionary<string, SchedulingEnvironmentValue> { ["m-1"] = MakeType("m-1", "Lecture") };
        var card = MakeCard([Meeting(day: 1, roomId: "r-1", meetingTypeId: "m-1", frequency: "odd")], rooms, types);

        var detail = Assert.Single(card.MeetingDetails);

        Assert.Equal("\nMon", card.MeetingDayLines);
        Assert.Equal("\n" + detail.StartTime, card.MeetingStartLines);
        Assert.Equal("\n" + detail.EndTime, card.MeetingEndLines);
        Assert.Equal("\n" + detail.Frequency, card.MeetingFrequencyLines);
        Assert.Equal("\nScience A101", card.MeetingRoomLines);
        Assert.Equal("\nLecture", card.MeetingTypeLines);

        // The frequency is non-empty here, so it must come through as itself (not a placeholder).
        Assert.NotEqual("", detail.Frequency);
    }

    [Fact]
    public void SeveralMeetings_StringsFollowMeetingDetailsOrder_NotTheOrderTheyWereAdded()
    {
        // Added out of order; the card sorts by day, then start time.
        var card = MakeCard(
        [
            Meeting(day: 3, start: 600),
            Meeting(day: 1, start: 780),
            Meeting(day: 1, start: 510),
        ]);

        // Sanity-check the order MeetingDetails settled on, then build the expectation from it.
        Assert.Equal(new[] { "Mon", "Mon", "Wed" }, card.MeetingDetails.Select(m => m.Day));

        Assert.Equal("\nMon\nMon\nWed", card.MeetingDayLines);
        Assert.Equal(
            string.Concat(card.MeetingDetails.Select(m => "\n" + m.StartTime)),
            card.MeetingStartLines);
        Assert.Equal(
            string.Concat(card.MeetingDetails.Select(m => "\n" + m.EndTime)),
            card.MeetingEndLines);

        // The two Monday meetings start at different times, so the Start column is not uniform;
        // together with the check above this shows the rows stay paired with their meetings.
        Assert.Equal(card.MeetingDetails[0].StartTime, card.MeetingStartLines.Split('\n')[1]);
        Assert.Equal(card.MeetingDetails[1].StartTime, card.MeetingStartLines.Split('\n')[2]);
        Assert.Equal(card.MeetingDetails[2].StartTime, card.MeetingStartLines.Split('\n')[3]);
        Assert.NotEqual(card.MeetingDetails[0].StartTime, card.MeetingDetails[1].StartTime);
    }

    [Fact]
    public void SeveralMeetings_RoomTypeAndFrequencyStayPairedWithTheirRow()
    {
        var rooms = new Dictionary<string, Room>
        {
            ["r-1"] = MakeRoom("r-1", "Science", "A101"),
            ["r-2"] = MakeRoom("r-2", "Arts", "B202"),
        };
        var types = new Dictionary<string, SchedulingEnvironmentValue>
        {
            ["m-1"] = MakeType("m-1", "Lecture"),
            ["m-2"] = MakeType("m-2", "Lab"),
        };
        var card = MakeCard(
        [
            Meeting(day: 2, roomId: "r-2", meetingTypeId: "m-2", frequency: "even"),
            Meeting(day: 1, roomId: "r-1", meetingTypeId: "m-1"),
        ],
        rooms, types);

        // Monday (r-1, Lecture, weekly) sorts ahead of Tuesday (r-2, Lab, even weeks).
        Assert.Equal("\nMon\nTue", card.MeetingDayLines);
        Assert.Equal("\nScience A101\nArts B202", card.MeetingRoomLines);
        Assert.Equal("\nLecture\nLab", card.MeetingTypeLines);
        Assert.Equal("\n" + Nbsp + "\n" + card.MeetingDetails[1].Frequency, card.MeetingFrequencyLines);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Empty values
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyValues_BecomeASingleNonBreakingSpace()
    {
        // No room, no meeting type, weekly (so no frequency text).
        var card = MakeCard([Meeting(day: 1)]);

        Assert.Equal("\n" + Nbsp, card.MeetingFrequencyLines);
        Assert.Equal("\n" + Nbsp, card.MeetingRoomLines);
        Assert.Equal("\n" + Nbsp, card.MeetingTypeLines);

        // The always-populated columns are untouched by the placeholder rule.
        Assert.Equal("\nMon", card.MeetingDayLines);
    }

    [Fact]
    public void RoomOrTypeIdMissingFromTheLookup_IsTreatedAsEmpty()
    {
        // A dangling id (e.g. a deleted room) resolves to "" in MeetingDetails; the column shows
        // the placeholder rather than an empty segment.
        var card = MakeCard([Meeting(day: 1, roomId: "gone", meetingTypeId: "also-gone")]);

        Assert.Equal("\n" + Nbsp, card.MeetingRoomLines);
        Assert.Equal("\n" + Nbsp, card.MeetingTypeLines);
    }

    [Fact]
    public void WhitespaceOnlyValue_BecomesASingleNonBreakingSpace()
    {
        // A room with no building and no number has the card label " " (a lone space); a
        // meeting type named with spaces and a tab is likewise blank. Both must become one NBSP.
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "", "") };
        var types = new Dictionary<string, SchedulingEnvironmentValue> { ["m-1"] = MakeType("m-1", "  \t ") };
        var card = MakeCard([Meeting(day: 1, roomId: "r-1", meetingTypeId: "m-1")], rooms, types);

        Assert.Equal("\n" + Nbsp, card.MeetingRoomLines);
        Assert.Equal("\n" + Nbsp, card.MeetingTypeLines);
    }

    [Fact]
    public void NoColumnEverContainsAnEmptySegment()
    {
        // Every line after the header must hold at least one character, so no line can collapse.
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "Science", "A101") };
        var card = MakeCard(
        [
            Meeting(day: 1, roomId: "r-1"),
            Meeting(day: 2),
            Meeting(day: 3, frequency: "odd"),
        ],
        rooms);

        foreach (var column in Columns(card))
        {
            var lines = column.Split('\n').Skip(1); // Skip(1): the text before the first "\n" is empty.
            Assert.All(lines, line => Assert.NotEqual("", line));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Alignment invariant: every column has the same number of lines
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixOfEmptyAndNonEmptyValues_EveryColumnHasMeetingsPlusOneLines()
    {
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "Science", "A101") };
        var types = new Dictionary<string, SchedulingEnvironmentValue> { ["m-1"] = MakeType("m-1", "Lab") };

        // Rows deliberately differ in which optional fields are filled in.
        var card = MakeCard(
        [
            Meeting(day: 1, roomId: "r-1", meetingTypeId: "m-1", frequency: "odd"), // everything
            Meeting(day: 2),                                                         // nothing optional
            Meeting(day: 3, roomId: "r-1"),                                          // room only
            Meeting(day: 4, meetingTypeId: "m-1"),                                   // type only
            Meeting(day: 5, frequency: "1,6,7"),                                     // frequency only
        ],
        rooms, types);

        int expected = card.MeetingDetails.Count + 1; // the +1 is the header line the view adds
        Assert.Equal(6, expected);

        Assert.All(Columns(card), column => Assert.Equal(expected, RenderedLineCount(column)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void EveryColumnHasMeetingsPlusOneLines_ForAnyMeetingCount(int count)
    {
        // Alternate filled and unfilled optional fields across the rows.
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "Science", "A101") };
        var meetings = Enumerable.Range(1, count)
            .Select(i => Meeting(day: i, start: 480 + i * 30, roomId: i % 2 == 0 ? "r-1" : null,
                                 frequency: i % 3 == 0 ? "even" : null))
            .ToList();
        var card = MakeCard(meetings, rooms);

        Assert.Equal(count, card.MeetingDetails.Count);
        Assert.All(Columns(card), column => Assert.Equal(count + 1, RenderedLineCount(column)));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Embedded newlines
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Sci\nence", "Sci ence 101")]
    [InlineData("Sci\r\nence", "Sci ence 101")]
    [InlineData("Sci\rence", "Sci ence 101")]
    [InlineData("Sci\n\nence", "Sci  ence 101")]
    public void NewlineInsideARoomName_IsFlattenedToASpace(string building, string expectedLabel)
    {
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", building, "101") };
        var card = MakeCard([Meeting(day: 1, roomId: "r-1")], rooms);

        Assert.Equal("\n" + expectedLabel, card.MeetingRoomLines);
    }

    [Fact]
    public void NewlineInsideAValue_NeverAddsALine_SoColumnsStayAligned()
    {
        // The room and meeting-type names both contain line breaks; all six columns must still
        // have exactly meetings + 1 lines, and no carriage returns may survive.
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "Sci\r\nence", "10\n1") };
        var types = new Dictionary<string, SchedulingEnvironmentValue> { ["m-1"] = MakeType("m-1", "Lab\r\n(2 hr)") };
        var card = MakeCard(
        [
            Meeting(day: 1, roomId: "r-1", meetingTypeId: "m-1"),
            Meeting(day: 2, roomId: "r-1"),
        ],
        rooms, types);

        Assert.All(Columns(card), column =>
        {
            Assert.Equal(card.MeetingDetails.Count + 1, RenderedLineCount(column));
            Assert.DoesNotContain('\r', column);
        });
        Assert.Equal("\nSci ence 10 1\nSci ence 10 1", card.MeetingRoomLines);
        Assert.Equal("\nLab (2 hr)\n" + Nbsp, card.MeetingTypeLines);
    }

    [Fact]
    public void ValueThatIsOnlyLineBreaks_BecomesANonBreakingSpace()
    {
        // A name that is nothing but line breaks is blank, so it takes the placeholder rule
        // rather than being flattened to a run of spaces.
        var types = new Dictionary<string, SchedulingEnvironmentValue> { ["m-1"] = MakeType("m-1", "\r\n\n") };
        var card = MakeCard([Meeting(day: 1, meetingTypeId: "m-1")], meetingTypes: types);

        Assert.Equal("\n" + Nbsp, card.MeetingTypeLines);
    }

    [Fact]
    public void MeetingDetails_KeepsTheRawValue_OnlyTheColumnStringsAreFlattened()
    {
        // The flattening belongs to the column strings alone; MeetingDetails (and anything else
        // that reads it) is unchanged.
        var rooms = new Dictionary<string, Room> { ["r-1"] = MakeRoom("r-1", "Sci\nence", "101") };
        var card = MakeCard([Meeting(day: 1, roomId: "r-1")], rooms);

        Assert.Equal("Sci\nence 101", Assert.Single(card.MeetingDetails).Room);
        Assert.Equal("\nSci ence 101", card.MeetingRoomLines);
    }
}
