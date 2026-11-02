namespace TermPoint.Tests.Harness;

/// <summary>One message (a message dialog or a banner) a computer showed, as the checker for invariants I7 and I8 sees it.</summary>
/// <param name="Computer">The computer's name ("bob").</param>
/// <param name="DisplayName">The computer's name as the story writes it ("Bob").</param>
/// <param name="Title">The dialog's title; empty for a banner.</param>
/// <param name="Text">The body text.</param>
/// <param name="IsQuestion">
/// True for a question that reports a problem (a "Try again / Close" choice such as "Couldn't Open the Database", see <see cref="MessageChecker.IsProblemQuestion"/>):
/// it counts for I8 (b), but not for I8 (a), because asking it again after "Try again" is the design.
/// </param>
/// <param name="Phase">
/// What was going on when it appeared: empty in an ordinary step; in a pause, "before the event" (the operation ran on to the crash point),
/// "while held: ..." (the event that ran while the operation was held) or "after release" (the operation went on). It tells the reader which
/// of the two actions of an interleaving showed the message.
/// </param>
internal sealed record SeenMessage(string Computer, string DisplayName, string Title, string Text, bool IsQuestion, string Phase);

/// <summary>
/// One time two different messages were shown on one computer in one step (invariant I8, part b), with where it happened.
/// </summary>
/// <param name="Computer">The computer's display name.</param>
/// <param name="First">The first identity in sorted order (<see cref="MessageChecker.IdentityOf"/>).</param>
/// <param name="Second">The second identity in sorted order.</param>
/// <param name="Run">The title of the run or walk it happened in.</param>
/// <param name="Step">The step number.</param>
/// <param name="Headline">The step's own story line (the event that was running).</param>
/// <param name="Phases">The phases of the two messages, in the order the messages appeared ("before the event; after release").</param>
internal sealed record MessagePairSighting(
    string Computer, string First, string Second, string Run, int Step, string Headline, string Phases);

/// <summary>
/// A pair of different messages that may appear together in one step on one computer, with the written reason why that is not "two messages for one
/// problem". The allow-list of invariant I8 (part b) is a list of these.
/// </summary>
/// <param name="First">One message's identity (<see cref="MessageChecker.IdentityOf"/>).</param>
/// <param name="Second">The other message's identity.</param>
/// <param name="Reason">Why the pair is two problems, not one.</param>
internal sealed record AllowedMessagePair(string First, string Second, string Reason);

/// <summary>
/// The allow-list for invariant I8 (b), "one message per problem": the pairs of different messages that may legitimately appear on one computer in
/// one step. Empty: every pair the harness has seen so far is one problem told twice, recorded as a known finding (F17, F18 and A13 in
/// <see cref="KnownFindings"/>) until it is fixed. Add a pair here only when its two messages really are about two different problems.
/// </summary>
internal static class MessageAllowList
{
    /// <summary>The allowed pairs, each with its reason.</summary>
    public static IReadOnlyList<AllowedMessagePair> Pairs { get; } = Array.Empty<AllowedMessagePair>();

    /// <summary>True when the two identities form an allowed pair (in either order).</summary>
    /// <param name="a">One identity.</param>
    /// <param name="b">The other identity.</param>
    public static bool Allows(string a, string b)
        => Pairs.Any(p => (p.First == a && p.Second == b) || (p.First == b && p.Second == a));
}

/// <summary>
/// Invariants I7 ("plain words": see <see cref="MessageWording"/>) and I8 ("one message per problem") of the harness, spec item 8.6, written against
/// project rule 4, "never confuse the user".
/// <list type="bullet">
/// <item><description><b>I7:</b> every message, choice and banner, title, text and button labels, is checked against <see cref="MessageWording"/> when it is shown
/// (<see cref="CheckWording"/>).</description></item>
/// <item><description><b>I8 (a):</b> the same title and text shown twice in one step on one computer is always a violation.</description></item>
/// <item><description><b>I8 (b):</b> two different messages (title plus first sentence) in one step on one computer is a violation unless the pair is on
/// <see cref="MessageAllowList"/>.</description></item>
/// </list>
/// Message dialogs and banners count for I8, and so does a question that REPORTS a problem (<see cref="IsProblemQuestion"/>: the "Try again / Close"
/// choice is a message with two buttons). Other choices, wait dialogs and pickers are questions the user answers, not statements, and do not count.
/// </summary>
internal static class MessageChecker
{
    /// <summary>
    /// True for a choice that is really a problem report with buttons: "Couldn't Open the Database" (Try again / Close) tells the person the database could not be
    /// opened. Such a choice takes part in I8 like a message. Add a title here when another error report is asked as a choice.
    /// </summary>
    /// <param name="title">The choice's title.</param>
    public static bool IsProblemQuestion(string title) => title == HarnessTitles.CouldntOpen;

    /// <summary>The key of a message: its title and the first sentence of its text ("Checkout Failed | Could not open the database for editing"). A banner has no title.</summary>
    /// <param name="title">The title ("" for a banner).</param>
    /// <param name="text">The text.</param>
    public static string IdentityOf(string title, string text)
        => (string.IsNullOrEmpty(title) ? "(banner)" : title) + " | " + MessagePromises.FirstSentence(text);

    /// <summary>
    /// Invariant I7: checks the title, the text and the button labels of one message, choice or banner against <see cref="MessageWording"/>.
    /// </summary>
    /// <param name="computer">The computer that showed it.</param>
    /// <param name="step">The current step.</param>
    /// <param name="kindName">"message", "question" or "banner", for the reason line.</param>
    /// <param name="title">The title ("" for a banner).</param>
    /// <param name="text">The text.</param>
    /// <param name="buttons">The button labels as the prompt recorded them ("a|b"); may be empty.</param>
    /// <param name="forbiddenFolders">The folders the harness's files live in, which the user must never see.</param>
    /// <returns>One violation per rule broken (none when the wording is plain).</returns>
    public static IReadOnlyList<Violation> CheckWording(
        Computer computer, int step, string kindName, string title, string text, string buttons, IEnumerable<string> forbiddenFolders)
    {
        var violations = new List<Violation>();
        var folders = forbiddenFolders.ToList();

        void Check(string part, string? what)
        {
            foreach (var reason in MessageWording.Reasons(what, folders))
            {
                var identity = IdentityOf(title, text);
                violations.Add(new Violation
                {
                    Step = step, Invariant = "I7", Kind = ViolationKind.MessageNotPlainWords,
                    Key = $"I7:{computer.Name}:{identity}:{part}:{reason}", Computer = computer.Name,
                    Reason = $"{computer.DisplayName}'s {kindName} \"{identity}\" {reason} in its {part}"
                });
            }
        }

        Check("title", title);
        Check("text", text);
        foreach (var label in buttons.Split('|', StringSplitOptions.RemoveEmptyEntries)) Check("button", label);
        return violations;
    }

    /// <summary>
    /// Invariant I8: judges the messages one computer showed in one step. (a) is always a violation; (b) is a violation unless the pair is on
    /// <see cref="MessageAllowList"/>, and is recorded in <paramref name="sightings"/> either way.
    /// </summary>
    /// <param name="seen">Every message and banner shown during the step, in order, for all computers.</param>
    /// <param name="step">The step.</param>
    /// <param name="run">The run's title.</param>
    /// <param name="headline">The step's own story line, or empty.</param>
    /// <param name="sightings">Receives one entry per pair of different messages found (for the report and the tests).</param>
    /// <returns>The violations.</returns>
    public static IReadOnlyList<Violation> CheckStep(
        IReadOnlyList<SeenMessage> seen, int step, string run, string headline, List<MessagePairSighting> sightings)
    {
        var violations = new List<Violation>();

        foreach (var perComputer in seen.GroupBy(m => m.Computer))
        {
            var messages = perComputer.ToList();
            var display = messages[0].DisplayName;

            // (a) the same message twice
            foreach (var twice in messages.Where(m => !m.IsQuestion).GroupBy(m => (m.Title, m.Text)).Where(g => g.Count() > 1))
            {
                var identity = IdentityOf(twice.Key.Title, twice.Key.Text);
                violations.Add(new Violation
                {
                    Step = step, Invariant = "I8", Kind = ViolationKind.MessageShownTwice,
                    Key = $"I8a:{step}:{perComputer.Key}:{identity}", Computer = perComputer.Key,
                    Reason = $"{display} was shown \"{identity}\" {twice.Count()} times in one step"
                });
            }

            // (b) two different messages
            var distinct = messages
                .GroupBy(m => IdentityOf(m.Title, m.Text))
                .Select(g => (Identity: g.Key, Phase: g.First().Phase))
                .ToList();
            for (var i = 0; i < distinct.Count; i++)
            {
                for (var j = i + 1; j < distinct.Count; j++)
                {
                    var (first, second) = string.CompareOrdinal(distinct[i].Identity, distinct[j].Identity) <= 0
                        ? (distinct[i], distinct[j]) : (distinct[j], distinct[i]);
                    var phases = string.Join("; ", new[] { distinct[i].Phase, distinct[j].Phase }.Select(p => p.Length == 0 ? "ordinary step" : p));

                    sightings.Add(new MessagePairSighting(display, first.Identity, second.Identity, run, step, headline, phases));

                    if (!MessageAllowList.Allows(first.Identity, second.Identity))
                    {
                        violations.Add(new Violation
                        {
                            Step = step, Invariant = "I8", Kind = ViolationKind.TwoMessagesForOneProblem,
                            Key = $"I8b:{step}:{perComputer.Key}:{first.Identity}:{second.Identity}", Computer = perComputer.Key,
                            Reason = $"{display} was shown two different messages in one step: \"{first.Identity}\" and \"{second.Identity}\"",
                            MessageIdentities = new[] { first.Identity, second.Identity }
                        });
                    }
                }
            }
        }
        return violations;
    }
}
