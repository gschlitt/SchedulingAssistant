using System.Globalization;
using System.Text;

namespace NetworkProbe;

/// <summary>
/// One reply from the partner, parsed. The partner times its own operation (<see cref="PartnerMs"/>); the runner times the
/// whole exchange on its own clock (<see cref="RoundTripMs"/>), so no clock is ever compared across computers.
/// </summary>
/// <param name="Ok">True for an <c>OK</c> reply, false for <c>ERR</c>.</param>
/// <param name="PartnerMs">How long the partner's own file call took, on the partner's stopwatch.</param>
/// <param name="Detail">The command's result text (OK replies only; empty for ERR).</param>
/// <param name="ExceptionType">The exception's type name (ERR replies only).</param>
/// <param name="HResult">The exception's HResult (ERR replies only).</param>
/// <param name="Message">The exception's message (ERR replies only).</param>
internal sealed record PartnerReply(bool Ok, double PartnerMs, string Detail, string ExceptionType, int HResult, string Message)
{
    /// <summary>Runner-side stopwatch time from sending the command to reading the reply; set by <see cref="PartnerClient"/>.</summary>
    public double RoundTripMs { get; init; }

    /// <summary>The outcome in a few words: <c>ok</c>, or <c>IOException 0x80070050</c>.</summary>
    public string ResultText => Ok ? "ok" : $"{ExceptionType} {ShareProtocol.HResultText(HResult)}";
}

/// <summary>
/// The control protocol between the share-semantics runner and partner (<c>--share-semantics</c>): one command per line,
/// fields separated by a TAB (so paths may contain spaces), one reply line per command. All paths in commands are RELATIVE
/// to the partner's <c>--root</c>; the partner refuses anything that resolves outside it.
///
/// <para>Commands (verb, then TAB-separated arguments):
/// <c>PING</c>; <c>INFO</c>; <c>SMBCONFIG</c>; <c>EXISTS path</c>; <c>CREATE path</c> (create or truncate, write a few bytes,
/// close); <c>CREATENEW path</c> (as CREATE but <c>FileMode.CreateNew</c>); <c>DELETE path</c>;
/// <c>HOLD path access share</c> (open an existing file and keep it open; replies the handle id);
/// <c>READ handle chunkBytes delayMs</c> (read the handle from the start in the background, hashing what it gets; replies
/// after the first chunk); <c>HASH handle [waitMs]</c> (wait for the reader to finish, report bytes, SHA-256 and any error);
/// <c>STOPREAD handle</c> (cancel the reader, then as HASH); <c>CLOSE handle</c>; <c>REPLACE-BY-RENAME tmp dest</c>
/// (<c>File.Move(tmp, dest, overwrite: true)</c>); <c>QUIT</c>.</para>
///
/// <para>Replies: <c>OK TAB ms TAB detail</c>, or <c>ERR TAB ms TAB exceptionType TAB 0xHRESULT TAB message</c>. An unknown
/// command is <c>ERR</c>.</para>
/// </summary>
internal static class ShareProtocol
{
    /// <summary>Field separator in commands and replies.</summary>
    public const char Sep = '\t';

    /// <summary>The command verbs.</summary>
    public static class Verbs
    {
        /// <summary>Round-trip probe; changes nothing.</summary>
        public const string Ping = "PING";
        /// <summary>Asks for the partner's machine, user, OS and root.</summary>
        public const string Info = "INFO";
        /// <summary>Asks the partner to run <c>Get-SmbClientConfiguration</c> and return the text.</summary>
        public const string SmbConfig = "SMBCONFIG";
        /// <summary>Does a file or folder exist (relative path)?</summary>
        public const string Exists = "EXISTS";
        /// <summary>Create or truncate a small file.</summary>
        public const string Create = "CREATE";
        /// <summary>Create a small file with <c>FileMode.CreateNew</c> (fails if it exists).</summary>
        public const string CreateNew = "CREATENEW";
        /// <summary>Delete a file.</summary>
        public const string Delete = "DELETE";
        /// <summary>Open an existing file and hold it open.</summary>
        public const string Hold = "HOLD";
        /// <summary>Start reading a held handle in the background.</summary>
        public const string Read = "READ";
        /// <summary>Cancel a handle's background reader and report what it read.</summary>
        public const string StopRead = "STOPREAD";
        /// <summary>Wait for a handle's reader to finish and report what it read.</summary>
        public const string Hash = "HASH";
        /// <summary>Close a held handle.</summary>
        public const string Close = "CLOSE";
        /// <summary>Move a temp file over a destination (overwrite), like TermPoint's save.</summary>
        public const string ReplaceByRename = "REPLACE-BY-RENAME";
        /// <summary>End this connection (the partner keeps listening).</summary>
        public const string Quit = "QUIT";
    }

    /// <summary>Formats an HResult as hex.</summary>
    /// <param name="hresult">The HResult.</param>
    /// <returns>For example <c>0x80070050</c>.</returns>
    public static string HResultText(int hresult) => $"0x{(uint)hresult:X8}";

    /// <summary>
    /// Makes text safe for a one-line reply: backslash, tab and line breaks are escaped.
    /// </summary>
    /// <param name="text">Any text.</param>
    /// <returns>The text on one line.</returns>
    public static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

    /// <summary>Reverses <see cref="Escape"/>.</summary>
    /// <param name="text">Escaped text from a reply.</param>
    /// <returns>The original text (without carriage returns).</returns>
    public static string Unescape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
            }
            else sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>Builds an OK reply line.</summary>
    /// <param name="ms">The partner's own elapsed time for the operation.</param>
    /// <param name="detail">The result text.</param>
    /// <returns>The line, without its terminator.</returns>
    public static string FormatOk(double ms, string detail)
        => $"OK{Sep}{ms.ToString("F2", CultureInfo.InvariantCulture)}{Sep}{Escape(detail)}";

    /// <summary>Builds an ERR reply line from an exception.</summary>
    /// <param name="ms">The partner's own elapsed time before the failure.</param>
    /// <param name="ex">What went wrong.</param>
    /// <returns>The line, without its terminator.</returns>
    public static string FormatErr(double ms, Exception ex)
        => $"ERR{Sep}{ms.ToString("F2", CultureInfo.InvariantCulture)}{Sep}{ex.GetType().Name}{Sep}{HResultText(ex.HResult)}{Sep}{Escape(ex.Message)}";

    /// <summary>
    /// Parses a reply line.
    /// </summary>
    /// <param name="line">The line as received.</param>
    /// <returns>The reply (<see cref="PartnerReply.RoundTripMs"/> not yet set).</returns>
    /// <exception cref="FormatException">The line is neither a well-formed OK nor ERR reply.</exception>
    public static PartnerReply ParseReply(string line)
    {
        var p = line.Split(Sep);
        if (p.Length >= 2 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
        {
            if (p[0] == "OK")
                return new PartnerReply(true, ms, p.Length > 2 ? Unescape(p[2]) : "", "", 0, "");
            if (p[0] == "ERR" && p.Length >= 5)
            {
                var hex = p[3].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? p[3][2..] : p[3];
                var h = int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                return new PartnerReply(false, ms, "", p[2], h, Unescape(p[4]));
            }
        }
        throw new FormatException($"The partner sent a line that is not a reply: '{line}'.");
    }
}
