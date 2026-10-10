using System.Text;

namespace TermPoint.Services;

/// <summary>
/// One logical CSV record as produced by <see cref="CsvRecordReader"/>: the raw record text
/// (still unsplit into fields) plus the physical line on which it starts.
/// </summary>
/// <param name="LineNumber">
/// 1-based physical line number of the first physical line of the record. For a record
/// without embedded newlines this is simply its own line; for a record whose quoted field
/// spans several lines it is the line the record began on (the next record's number is
/// larger by the number of physical lines consumed).
/// </param>
/// <param name="Text">
/// The record text. Physical lines of a multi-line record are joined with <c>"\n"</c>
/// (regardless of whether the source used LF or CRLF). Field splitting is left to
/// <see cref="SharedScheduleCsvParser.ParseCsvRow"/>.
/// </param>
internal sealed record CsvRecord(int LineNumber, string Text);

/// <summary>
/// Thrown by <see cref="CsvRecordReader"/> when the input ends while a quoted field is still
/// open, i.e. the file has an unmatched <c>"</c> (malformed, or truncated mid-field).
/// <see cref="Exception.Message"/> is the end-user wording (<see cref="Reason"/>) and
/// deliberately does <em>not</em> contain the line number: some callers' UI already prefixes
/// "Line N:" (the CSV Import dialogs), others have no such prefix and add it themselves
/// from <see cref="LineNumber"/>.
/// </summary>
internal sealed class CsvFormatException : FormatException
{
    /// <summary>
    /// The user-facing description of the problem, without a line number. Defined once here so
    /// every caller and test refers to the same text.
    /// </summary>
    public const string Reason = "Unmatched quote. The file may be malformed or truncated.";

    /// <summary>
    /// 1-based physical line on which the unmatched quote's record starts. By construction that
    /// first physical line contains a quote that opens a quoted field (at a field start) and is
    /// not closed on that line, so the offending quote is on this line — or, if the record
    /// already spanned legitimate multi-line quoted fields, on a later line of the same record.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>Creates the exception for an unmatched quote in the record starting at <paramref name="lineNumber"/>.</summary>
    /// <param name="lineNumber">1-based physical line where the unterminated record starts.</param>
    public CsvFormatException(int lineNumber)
        : base(Reason)
    {
        LineNumber = lineNumber;
    }
}

/// <summary>
/// Reads logical CSV records from a <see cref="TextReader"/>, joining physical lines when a
/// quoted field contains an embedded newline. Shared by <see cref="CsvImportParser"/> and
/// <see cref="SharedScheduleCsvParser"/>.
///
/// <para>Work is linear in the size of the input: a small scanner state
/// (<c>inQuotes</c>, <c>fieldStart</c>) is carried from one physical line to the next, and each
/// character is examined exactly once.</para>
///
/// <para><b>The scanner mirrors <see cref="SharedScheduleCsvParser.ParseCsvRow"/> exactly</b>,
/// because the reader's record boundaries must agree with how the record is then split:</para>
/// <list type="bullet">
///   <item>A quoted field opens only when <c>"</c> is the <em>first character of a field</em>
///         (start of the record, or right after a comma). A <c>"</c> anywhere else in an
///         unquoted field — <c>5" ruler</c>, <c>ab"c</c>, or after leading whitespace as in
///         <c> "x"</c> — is literal and never starts a multi-line join.</item>
///   <item>Inside a quoted field, <c>""</c> (two quotes on the same physical line) is an escaped
///         quote and the field stays open; any other <c>"</c> — including one that is the last
///         character of a physical line — closes it.</item>
///   <item>After a closing quote a new field starts immediately (a comma, if present, merely
///         separates it), exactly as <c>ParseCsvRow</c> resumes scanning there.</item>
/// </list>
///
/// <para>If the input ends while a quoted field is still open, <see cref="ReadRecord"/> throws
/// <see cref="CsvFormatException"/> rather than returning the joined remainder of the file.</para>
/// </summary>
internal sealed class CsvRecordReader
{
    private readonly TextReader _reader;

    /// <summary>1-based physical line number of the next line <see cref="_reader"/> will return.</summary>
    private int _nextLineNumber;

    /// <summary>
    /// Creates a reader over <paramref name="reader"/>. The caller keeps ownership of the
    /// underlying reader (this class never disposes it).
    /// </summary>
    /// <param name="reader">Source of physical lines; positioned at the first line to read.</param>
    /// <param name="firstLineNumber">
    /// 1-based physical line number of the first line <paramref name="reader"/> will return.
    /// Pass a value greater than 1 when the caller has already consumed leading lines (for
    /// example a header comment) so reported line numbers match the file on disk.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="firstLineNumber"/> is less than 1.</exception>
    public CsvRecordReader(TextReader reader, int firstLineNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstLineNumber, 1);
        _reader = reader;
        _nextLineNumber = firstLineNumber;
    }

    /// <summary>
    /// Reads the next logical record. A blank physical line is returned as a record with
    /// empty <see cref="CsvRecord.Text"/> (callers decide whether to skip it) so that line
    /// numbering stays exact.
    /// </summary>
    /// <returns>The next record, or null at end of input (including on every call after a
    /// <see cref="CsvFormatException"/>, since the underlying reader is then exhausted).</returns>
    /// <exception cref="CsvFormatException">
    /// The input ended while a quoted field was still open. The exception reports the line
    /// on which that record started.
    /// </exception>
    public CsvRecord? ReadRecord()
    {
        int startLine = _nextLineNumber;
        string? line = _reader.ReadLine();
        if (line is null) return null;
        _nextLineNumber++;

        // Common case: the line leaves no quote open, so it is a complete record by itself
        // and no buffer is needed.
        bool inQuotes = ScanLine(line, startsInQuotes: false);
        if (!inQuotes) return new CsvRecord(startLine, line);

        // A quoted field continues onto the next physical line(s). Append each new line once
        // and scan only that new line, so cost stays linear however long the record grows.
        var text = new StringBuilder(line);
        while (inQuotes)
        {
            string? next = _reader.ReadLine();
            if (next is null)
                throw new CsvFormatException(startLine);

            _nextLineNumber++;
            text.Append('\n').Append(next);
            inQuotes = ScanLine(next, startsInQuotes: true);
        }

        return new CsvRecord(startLine, text.ToString());
    }

    /// <summary>
    /// Advances the quote state across one physical line, following the same field rules as
    /// <see cref="SharedScheduleCsvParser.ParseCsvRow"/> (see the class remarks): a quoted field
    /// opens only on a <c>"</c> at a field start; inside one, <c>""</c> is an escape and any
    /// other <c>"</c> closes it; elsewhere a <c>"</c> is an ordinary character.
    ///
    /// <para>Only <c>inQuotes</c> needs to be carried between physical lines. A record continues
    /// onto the next line only while a quoted field is open, and inside a quoted field
    /// "at a field start" is false by definition; when the line ends outside quotes the record
    /// is complete and the next record starts afresh at a field start.</para>
    /// </summary>
    /// <param name="line">The physical line to scan (no line terminator).</param>
    /// <param name="startsInQuotes">
    /// Whether a quoted field is still open from the previous physical line of this record
    /// (false for the first line of a record).
    /// </param>
    /// <returns>Whether a quoted field is still open at the end of the line.</returns>
    private static bool ScanLine(string line, bool startsInQuotes)
    {
        bool inQuotes = startsInQuotes;
        bool fieldStart = !startsInQuotes;   // the first character of a new record starts a field

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c != '"') continue;

                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;                     // escaped quote ("") — consume both, stay open
                    continue;
                }

                inQuotes = false;            // closing quote (also when it is the last char on the line)
                fieldStart = true;           // ParseCsvRow resumes a new field right after it
            }
            else if (c == '"' && fieldStart)
            {
                inQuotes = true;             // opening quote of a quoted field
                fieldStart = false;
            }
            else
            {
                // Ordinary character, or a literal quote inside an unquoted field.
                // Only a comma lets the next character start a field.
                fieldStart = c == ',';
            }
        }

        return inQuotes;
    }
}
