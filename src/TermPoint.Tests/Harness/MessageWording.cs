using System.Text.RegularExpressions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// Invariant I7, "plain words" (spec item 8.6; project rule 4, "never confuse the user"): the rules the text of every message, choice and banner is
/// checked against. A message must not use the program's own jargon ("working copy", "local copy"), the word "safe" (it promises more than the
/// program can know; the messages say what was kept and where), a file path (a bare file name the user chose is fine), or log text (exception names,
/// stack traces, error codes, namespaces, GUIDs).
///
/// <para>The rules live here, in one static class with no dependency on the harness's world, so the harness (<see cref="MessageChecker"/>) and the unit
/// tests (<c>MessageWordingTests</c>) share them.</para>
/// </summary>
internal static class MessageWording
{
    /// <summary>The program's own jargon for the files it works on, which means nothing to the user: "working copy" and "local copy".</summary>
    private static readonly Regex Jargon = new(@"\b(working|local)\s+copy\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The whole word "safe" (not "unsafe" or "safely").</summary>
    private static readonly Regex SafeWord = new(@"\bsafe\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A drive-letter path: a letter not preceded by another letter, then a colon and a slash or backslash ("C:\" or "C:/", but not "http://").</summary>
    private static readonly Regex DrivePath = new(@"(?<![A-Za-z])[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);

    /// <summary>A UNC path: two backslashes followed by a server name.</summary>
    private static readonly Regex UncPath = new(@"\\\\[^\s\\]", RegexOptions.CultureInvariant);

    /// <summary>The opening of a stack-trace line: "at Namespace.Type.Method(" (the plain "   at " form is tested separately).</summary>
    private static readonly Regex StackFrame = new(@"\bat [A-Za-z_][\w.<>`+]*\.[\w<>`]+\(", RegexOptions.CultureInvariant);

    /// <summary>A .NET namespace from the framework or the program, followed by a name ("System.IO", "TermPoint.Services"); a sentence that merely ends in "system." does not match.</summary>
    private static readonly Regex Namespace = new(@"\b(System|TermPoint|Microsoft)\.[A-Za-z]", RegexOptions.CultureInvariant);

    /// <summary>A GUID in its usual 8-4-4-4-12 hexadecimal form.</summary>
    private static readonly Regex GuidShape = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Says in what ways one piece of message text breaks the plain-words rules.
    /// </summary>
    /// <param name="text">A title, a body, or a button label. Null or empty breaks nothing.</param>
    /// <param name="forbiddenFolders">
    /// Folders the user must never be shown: the harness's temp, working and share folders. A text that contains one of them, written
    /// with either kind of slash and in any case, is reported. Null or empty entries are ignored.
    /// </param>
    /// <returns>One short reason per rule broken, in a fixed order (empty when the text is plain).</returns>
    public static IReadOnlyList<string> Reasons(string? text, IEnumerable<string>? forbiddenFolders = null)
    {
        var reasons = new List<string>();
        if (string.IsNullOrEmpty(text)) return reasons;

        if (Jargon.Match(text) is { Success: true } jargon) reasons.Add($"says \"{jargon.Value}\"");
        if (SafeWord.IsMatch(text)) reasons.Add("uses the word \"safe\"");

        if (DrivePath.IsMatch(text)) reasons.Add("shows a drive path");
        if (UncPath.IsMatch(text)) reasons.Add("shows a network path");
        if (ContainsAnyFolder(text, forbiddenFolders)) reasons.Add("shows one of the program's folders");

        if (text.Contains("Exception", StringComparison.Ordinal)) reasons.Add("shows an exception name");
        if (text.Contains("   at ", StringComparison.Ordinal) || StackFrame.IsMatch(text)) reasons.Add("shows a stack trace");
        if (text.Contains("HRESULT", StringComparison.OrdinalIgnoreCase)) reasons.Add("shows an HRESULT");
        if (text.Contains("0x8007", StringComparison.OrdinalIgnoreCase)) reasons.Add("shows an error code (0x8007...)");
        if (Namespace.Match(text) is { Success: true } ns) reasons.Add($"shows a program name (\"{ns.Value}...\")");
        if (GuidShape.IsMatch(text)) reasons.Add("shows a GUID");

        return reasons;
    }

    /// <summary>True when the text contains any of the folders, compared without regard to case or to the kind of slash.</summary>
    /// <param name="text">The text.</param>
    /// <param name="folders">The folders (null or empty entries are skipped).</param>
    private static bool ContainsAnyFolder(string text, IEnumerable<string>? folders)
    {
        if (folders is null) return false;

        var normalisedText = text.Replace('/', '\\');
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            if (normalisedText.Contains(folder.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
