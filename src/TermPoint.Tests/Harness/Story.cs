using System.Text;

namespace TermPoint.Tests.Harness;

/// <summary>One line of the story: something a person did, a dialog or message they saw, or something the world did.</summary>
internal sealed class StoryEntry
{
    /// <summary>The funnel step the line belongs to (1, 2, ...).</summary>
    public required int Step { get; init; }

    /// <summary>0 for the step's own line; 1 for what happened inside it (dialogs, messages, events a dialog's script triggered).</summary>
    public required int Indent { get; init; }

    /// <summary>The sentence. An action's line grows a result ("... — Success") when the action ends.</summary>
    public required string Text { get; set; }
}

/// <summary>
/// The story printer of the multi-computer harness (spec item 9.3, step 9.3b): a chronological narrative in plain
/// English of what each person did, the dialogs they saw and how they answered, and what the world did (time passing,
/// the network dropping, a computer crashing), followed by "Where every edit is" and the violations. It is always
/// written to the test's output, and on a failure it is the exception message.
///
/// <para>The entries are added from the funnel (<see cref="World"/>) and from the observers of prompts, banners and
/// crash points; they may arrive from a pool thread (a lock release, a heartbeat), so adding is under a lock.</para>
/// </summary>
internal sealed class Story
{
    /// <summary>Guards <see cref="_entries"/>.</summary>
    private readonly object _gate = new();

    /// <summary>The lines so far, in order.</summary>
    private readonly List<StoryEntry> _entries = new();

    /// <summary>Appends a line.</summary>
    /// <param name="step">The funnel step it belongs to.</param>
    /// <param name="indent">0 for a step's own line, 1 for a detail inside it.</param>
    /// <param name="text">The sentence.</param>
    /// <returns>The entry, so an action can append its result later.</returns>
    public StoryEntry Add(int step, int indent, string text)
    {
        var entry = new StoryEntry { Step = step, Indent = indent, Text = text };
        lock (_gate) _entries.Add(entry);
        return entry;
    }

    /// <summary>Renders the whole story.</summary>
    /// <param name="title">What the story is of (the walk's name), or empty.</param>
    /// <param name="editPlaces">One line per edit: "bob-1: the shared database, ...".</param>
    /// <param name="violations">One line per violation: "I2 / F1 (step 9): ...".</param>
    /// <param name="notes">Extra closing lines (declared and occurred findings, promises still pending); may be empty.</param>
    /// <returns>The printed story.</returns>
    public string Render(
        string title, IEnumerable<string> editPlaces, IEnumerable<string> violations, IEnumerable<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrEmpty(title) ? "THE STORY" : $"THE STORY of {title}");

        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                sb.AppendLine(entry.Indent == 0
                    ? $"{entry.Step,3}. {entry.Text}"
                    : $"       - {entry.Text}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("WHERE EVERY EDIT IS");
        var any = false;
        foreach (var line in editPlaces) { sb.AppendLine("  " + line); any = true; }
        if (!any) sb.AppendLine("  (no edits were made)");

        sb.AppendLine();
        sb.AppendLine("VIOLATIONS");
        any = false;
        foreach (var line in violations) { sb.AppendLine("  " + line); any = true; }
        if (!any) sb.AppendLine("  (none)");

        foreach (var note in notes) sb.AppendLine(note);
        return sb.ToString();
    }
}
