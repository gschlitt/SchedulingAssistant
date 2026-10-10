using System.Diagnostics;
using System.Text;
using TermPoint.Services;
using Xunit;
using Xunit.Abstractions;

namespace TermPoint.Tests;

/// <summary>
/// Unit tests for <see cref="CsvRecordReader"/>, the logical-record reader shared by
/// <see cref="CsvImportParser"/> and <see cref="SharedScheduleCsvParser"/>: quote-state
/// tracking across physical lines, physical line numbering, and fail-fast behaviour on
/// an unterminated quote.
/// </summary>
public class CsvRecordReaderTests
{
    /// <summary>Generous ceiling for the linear-time test; a tripwire for quadratic regressions.</summary>
    private const int LinearTimeCeilingMs = 2000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the fixture; <paramref name="output"/> receives timing measurements.</summary>
    public CsvRecordReaderTests(ITestOutputHelper output) => _output = output;

    /// <summary>Reads every record from <paramref name="text"/> with a fresh reader.</summary>
    /// <param name="text">The CSV text.</param>
    /// <param name="firstLineNumber">Physical line number of the first line of <paramref name="text"/>.</param>
    /// <exception cref="CsvFormatException">The text ends inside an open quoted field.</exception>
    private static List<CsvRecord> ReadAll(string text, int firstLineNumber = 1)
    {
        var reader = new CsvRecordReader(new StringReader(text), firstLineNumber);
        var records = new List<CsvRecord>();
        while (reader.ReadRecord() is { } record)
            records.Add(record);
        return records;
    }

    /// <summary>Renders <paramref name="s"/> for failure messages, making CR/LF visible.</summary>
    private static string Show(string s) => "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    /// <summary>
    /// Asserts that <paramref name="actual"/> (a <c>ParseCsvRow</c> result) holds the
    /// <paramref name="expected"/> fields. <c>ParseCsvRow</c> may append ONE extra empty field
    /// after the last field of a row that does not end in a comma (a long-standing quirk that is
    /// harmless because callers index fields by header column), so extra trailing empties —
    /// at most one — are tolerated.
    /// </summary>
    private static void AssertFieldsEqual(string[] expected, string[] actual, string input)
    {
        bool ok = actual.Length >= expected.Length
               && actual.Length <= expected.Length + 1
               && expected.Zip(actual).All(p => p.First == p.Second)
               && actual.Skip(expected.Length).All(f => f.Length == 0);
        Assert.True(ok,
            $"Input {Show(input)}: expected fields [{string.Join(" | ", expected.Select(Show))}] " +
            $"but ParseCsvRow gave [{string.Join(" | ", actual.Select(Show))}]");
    }

    /// <summary>Fails the test, naming <paramref name="input"/>, unless <paramref name="condition"/> holds.</summary>
    /// <param name="condition">The property being checked.</param>
    /// <param name="input">The input under test (shown only on failure, so the exhaustive loop stays cheap).</param>
    /// <param name="what">A constant description of the property.</param>
    private static void Require(bool condition, string input, string what)
    {
        if (!condition) Assert.Fail($"Input {Show(input)}: {what}");
    }

    /// <summary>Splits <paramref name="text"/> into physical lines the same way the reader's source does (<see cref="TextReader.ReadLine"/>).</summary>
    private static List<string> SplitPhysicalLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }

    /// <summary>Joins <paramref name="count"/> physical lines starting at <paramref name="first"/> with <c>"\n"</c>, as the reader does.</summary>
    private static string JoinLines(List<string> lines, int first, int count) =>
        string.Join("\n", lines.Skip(first).Take(count));

    /// <summary>
    /// Asks <c>ParseCsvRow</c> itself whether <paramref name="text"/> ends inside a quoted field
    /// that is still open: append <c>",ZZZ"</c>. If no field is left open, the comma separates and
    /// <c>ZZZ</c> becomes a field of its own; if one is open, the comma and <c>ZZZ</c> are
    /// swallowed into it (so no field equals exactly <c>ZZZ</c>).
    /// </summary>
    private static bool EndsInsideOpenQuotedField(string text) =>
        !SharedScheduleCsvParser.ParseCsvRow(text + ",ZZZ").Contains("ZZZ");

    /// <summary>
    /// Runs a fresh reader over <paramref name="input"/> and checks every record boundary it
    /// chose against <see cref="EndsInsideOpenQuotedField"/>: a record must end at the first
    /// physical line after which no quoted field is open, and a thrown
    /// <see cref="CsvFormatException"/> must correspond to a field that is open at EOF.
    /// </summary>
    private static void AssertReaderAgreesWithParseCsvRow(string input)
    {
        var lines = SplitPhysicalLines(input);
        var reader = new CsvRecordReader(new StringReader(input));
        int consumed = 0;   // physical lines used up by the records read so far

        while (true)
        {
            CsvRecord? record;
            try
            {
                record = reader.ReadRecord();
            }
            catch (CsvFormatException ex)
            {
                // Every prefix of what is left — including all of it — must end inside an open field.
                for (int count = 1; count <= lines.Count - consumed; count++)
                    Require(EndsInsideOpenQuotedField(JoinLines(lines, consumed, count)), input,
                        "reader threw, but ParseCsvRow sees no open quoted field at the end of the remaining text");
                Require(ex.LineNumber == consumed + 1, input, "unterminated-quote line number is not the record's start line");
                return;
            }

            if (record is null)
            {
                Require(consumed == lines.Count, input, "reader stopped before consuming every physical line");
                return;
            }

            int lineCount = record.Text.Count(c => c == '\n') + 1;
            Require(record.LineNumber == consumed + 1, input, "record start line number is wrong");
            Require(record.Text == JoinLines(lines, consumed, lineCount), input, "record text is not the joined physical lines");
            for (int count = 1; count < lineCount; count++)
                Require(EndsInsideOpenQuotedField(JoinLines(lines, consumed, count)), input,
                    "reader continued a record although ParseCsvRow sees no open quoted field");
            Require(!EndsInsideOpenQuotedField(record.Text), input,
                "reader ended a record although ParseCsvRow sees a quoted field still open");
            consumed += lineCount;
        }
    }

    [Fact]
    public void ReadRecord_EmptyInput_ReturnsNull()
    {
        var reader = new CsvRecordReader(new StringReader(""));

        Assert.Null(reader.ReadRecord());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReadRecord_PlainLines_OneRecordPerLineWithPhysicalLineNumbers(string newline)
    {
        var records = ReadAll($"a,b{newline}c,d{newline}e,f{newline}");

        Assert.Equal(new[] { (1, "a,b"), (2, "c,d"), (3, "e,f") },
                     records.Select(r => (r.LineNumber, r.Text)).ToArray());
    }

    [Fact]
    public void ReadRecord_NoTrailingNewline_LastRecordStillReturned()
    {
        var records = ReadAll("a,b\nc,d");

        Assert.Equal(2, records.Count);
        Assert.Equal("c,d", records[1].Text);
    }

    [Fact]
    public void ReadRecord_BlankLine_ReturnedAsEmptyRecordAndKeepsNumbering()
    {
        var records = ReadAll("a\n\nb\n");

        Assert.Equal(new[] { (1, "a"), (2, ""), (3, "b") },
                     records.Select(r => (r.LineNumber, r.Text)).ToArray());
    }

    [Fact]
    public void ReadRecord_FirstLineNumber_IsHonored()
    {
        var records = ReadAll("a\nb\n", firstLineNumber: 10);

        Assert.Equal(new[] { 10, 11 }, records.Select(r => r.LineNumber).ToArray());
    }

    /// <summary>
    /// Bug B regression: every <c>"</c> must be counted, so escaped pairs (<c>""</c>) never
    /// leave a balanced quoted field looking unterminated.
    /// </summary>
    [Theory]
    [InlineData("\"12\"\" ruler\",1")]       // odd number of escaped quotes
    [InlineData("\"a\"\"b\",1")]             // odd number of escaped quotes
    [InlineData("\"a\"\"b\"\"c\",1")]        // two escaped pairs
    [InlineData("\"\",1")]                   // empty quoted field
    [InlineData("1,\"\"")]                   // empty quoted field at the end of the row
    [InlineData("\"\"\"quoted\"\"\",1")]     // field that is itself wrapped in escaped quotes
    [InlineData("\"\"\"\",1")]               // field containing just one quote character
    public void ReadRecord_BalancedQuotedFieldsWithEscapes_StayOneRecord(string row)
    {
        var records = ReadAll($"{row}\nnext,row\n");

        Assert.Equal(new[] { (1, row), (2, "next,row") },
                     records.Select(r => (r.LineNumber, r.Text)).ToArray());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReadRecord_QuotedFieldSpanningThreeLines_IsOneRecordJoinedWithLf(string newline)
    {
        var text = $"h1,h2{newline}a,\"line one{newline}line two{newline}line three\",b{newline}next,row{newline}";

        var records = ReadAll(text);

        Assert.Equal(3, records.Count);
        Assert.Equal((1, "h1,h2"), (records[0].LineNumber, records[0].Text));
        // The joined record always uses "\n", whatever the source's line terminator was.
        Assert.Equal((2, "a,\"line one\nline two\nline three\",b"), (records[1].LineNumber, records[1].Text));
        // Physical lines 2, 3 and 4 were consumed by the record above, so the next one is line 5.
        Assert.Equal((5, "next,row"), (records[2].LineNumber, records[2].Text));
    }

    [Fact]
    public void ReadRecord_EscapedQuoteInsideMultiLineField_DoesNotEndTheRecordEarly()
    {
        // The "" on the continuation line is an escape inside the open field, not a close.
        var records = ReadAll("a,\"x\ny\"\"z\nw\",b\nnext\n");

        Assert.Equal(new[] { (1, "a,\"x\ny\"\"z\nw\",b"), (4, "next") },
                     records.Select(r => (r.LineNumber, r.Text)).ToArray());
    }

    [Fact]
    public void ReadRecord_UnterminatedQuote_ThrowsWithLineWhereRecordStarted()
    {
        var ex = Assert.Throws<CsvFormatException>(() => ReadAll("a,b\nc,\"open\nmore\nlast\n"));

        Assert.Equal(2, ex.LineNumber);
        // The message is the line-free reason; callers add "Line N:" themselves where their UI needs it.
        Assert.Equal(CsvFormatException.Reason, ex.Message);
    }

    [Fact]
    public void ReadRecord_UnterminatedQuoteOnLastLineWithoutNewline_ThrowsWithThatLine()
    {
        var ex = Assert.Throws<CsvFormatException>(() => ReadAll("a\n\"b"));

        Assert.Equal(2, ex.LineNumber);
    }

    [Fact]
    public void ReadRecord_UnterminatedQuote_ReportsPhysicalLineRelativeToFirstLineNumber()
    {
        var ex = Assert.Throws<CsvFormatException>(() => ReadAll("ok\nx,\"open\nmore\n", firstLineNumber: 5));

        Assert.Equal(6, ex.LineNumber);
    }

    /// <summary>
    /// A quote that opens a quoted field at a field start but is never closed is an error,
    /// reported at the line where that record started — even when later lines contain more
    /// quote characters (here escaped <c>""</c> pairs, which keep the field open) and the
    /// open field therefore runs all the way to the end of the input.
    /// </summary>
    [Fact]
    public void ReadRecord_UnterminatedQuoteAtFieldStartWithLaterEscapedQuotes_ReportsRecordStartLine()
    {
        var text = "ok,1\n"                   // line 1
                 + "x,\"stray,1\n"           // line 2: opens a quoted field at a field start
                 + "say \"\"hi\"\",2\n"      // line 3: escaped pairs inside the open field
                 + "t,u\n";                  // line 4

        var ex = Assert.Throws<CsvFormatException>(() => ReadAll(text));

        Assert.Equal(2, ex.LineNumber);
    }

    /// <summary>
    /// A <c>"</c> that is not the first character of a field is literal, exactly as
    /// <c>ParseCsvRow</c> treats it, so it never makes the reader join lines — whether it is
    /// mid-field (<c>ab"c</c>, <c>5" ruler</c>), after leading whitespace (<c> "x"</c>,
    /// <c>a, "b</c>), or after a closing quote (<c>"a"x"y"</c>, <c>"a" "b</c>).
    /// </summary>
    [Theory]
    [InlineData("ab\"c,1")]
    [InlineData("CHEM101,5\" ruler,x")]
    [InlineData("a\"")]
    [InlineData("a\"\"b,1")]            // doubled quote in an UNquoted field is two literals, not an escape
    [InlineData(" \"x\",2")]
    [InlineData("a, \"b")]              // unbalanced, but the quote is not at a field start
    [InlineData("\"a\"x,3")]
    [InlineData("\"a\"x\"y\",b")]
    [InlineData("\"a\" \"b")]
    public void ReadRecord_QuoteNotAtFieldStart_IsLiteralAndNeverJoinsLines(string row)
    {
        var records = ReadAll($"{row}\nnext,row\n");

        Assert.Equal(new[] { (1, row), (2, "next,row") },
                     records.Select(r => (r.LineNumber, r.Text)).ToArray());
    }

    /// <summary>
    /// Two unquoted inch marks on different lines used to "balance" each other under
    /// every-quote-toggles scanning, merging lines 3-7 into one record and silently losing
    /// the rows in between. Each line must be its own record, text intact.
    /// </summary>
    [Fact]
    public void ReadRecord_UnquotedInchMarksOnDifferentLines_AreSeparateRecords()
    {
        var lines = new[]
        {
            "h1,h2,h3",                      // 1
            "A1,a,1",                        // 2
            "CHEM101,5\" ruler,x",           // 3: stray quote #1
            "B,b,2",                         // 4
            "C,c,3",                         // 5
            "D,d,4",                         // 6
            "CHEM102,3\" tube,y",            // 7: stray quote #2
            "E,e,5",                         // 8
        };

        var records = ReadAll(string.Join("\n", lines) + "\n");

        Assert.Equal(lines.Length, records.Count);
        for (int i = 0; i < lines.Length; i++)
        {
            Assert.Equal(i + 1, records[i].LineNumber);
            Assert.Equal(lines[i], records[i].Text);
        }
        // ...and each splits into the expected fields, with the literal quote preserved.
        Assert.Equal("5\" ruler", SharedScheduleCsvParser.ParseCsvRow(records[2].Text)[1]);
        Assert.Equal("3\" tube", SharedScheduleCsvParser.ParseCsvRow(records[6].Text)[1]);
    }

    /// <summary>
    /// Agreement table: for tricky single- and multi-line inputs, the reader's record
    /// boundaries must produce record texts that <c>ParseCsvRow</c> splits into the
    /// expected fields. A boundary disagreement (a record merged too much or too little)
    /// shows up as a wrong record count or wrong fields.
    /// </summary>
    [Fact]
    public void ReadRecord_RecordTextsSplitIntoExpectedFields_ByParseCsvRow()
    {
        var cases = new (string Input, string[][] Fields)[]
        {
            ("a,b\nc,d\n",                      new[] { new[] { "a", "b" }, new[] { "c", "d" } }),
            ("CHEM101,5\" ruler,x\ny,z\n",      new[] { new[] { "CHEM101", "5\" ruler", "x" }, new[] { "y", "z" } }),
            ("ab\"c,d\n",                       new[] { new[] { "ab\"c", "d" } }),
            ("a\"\nb\"\n",                      new[] { new[] { "a\"" }, new[] { "b\"" } }),
            (" \"x\",2\nnext\n",                new[] { new[] { " \"x\"", "2" }, new[] { "next" } }),
            ("\"a\"x,3\nnext\n",                new[] { new[] { "a", "x", "3" }, new[] { "next" } }),
            ("\"a\"x\"y\",b\nnext\n",           new[] { new[] { "a", "x\"y\"", "b" }, new[] { "next" } }),
            ("\"a\" \"b\",c\n",                 new[] { new[] { "a", " \"b\"", "c" } }),
            ("a,\"b\"c\n",                      new[] { new[] { "a", "b", "c" } }),
            ("\"12\"\" ruler\",1\nnext\n",      new[] { new[] { "12\" ruler", "1" }, new[] { "next" } }),
            ("\"\",1\nnext\n",                  new[] { new[] { "", "1" }, new[] { "next" } }),
            ("\"a\"\"b\"\n",                    new[] { new[] { "a\"b" } }),
            ("a,\"\"\"q\"\"\",z\n",             new[] { new[] { "a", "\"q\"", "z" } }),
            ("a,\"x\ny\",b\nnext\n",            new[] { new[] { "a", "x\ny", "b" }, new[] { "next" } }),
            ("\"x\ny\nz\"\nnext\n",             new[] { new[] { "x\ny\nz" }, new[] { "next" } }),
            ("\"a\nb\"\"c\nd\",e\n",            new[] { new[] { "a\nb\"c\nd", "e" } }),
            ("a, \"b\nc,d\n",                   new[] { new[] { "a", " \"b" }, new[] { "c", "d" } }),
            ("a,\"x\r\ny\",b\r\nnext\r\n",      new[] { new[] { "a", "x\ny", "b" }, new[] { "next" } }),
        };

        foreach (var (input, expectedFields) in cases)
        {
            var records = ReadAll(input);

            Assert.True(expectedFields.Length == records.Count,
                $"Input {Show(input)}: expected {expectedFields.Length} record(s) but got {records.Count}");
            for (int i = 0; i < expectedFields.Length; i++)
                AssertFieldsEqual(expectedFields[i], SharedScheduleCsvParser.ParseCsvRow(records[i].Text), input);
        }
    }

    /// <summary>
    /// Exhaustive differential check against <c>ParseCsvRow</c> itself: over EVERY input of up
    /// to 7 characters drawn from <c>a " , space newline</c>, the reader must end a record at a
    /// physical line exactly when <c>ParseCsvRow</c> would see no quoted field left open there.
    /// (A field is "left open" iff a sentinel field appended after the text gets swallowed into
    /// it — see <see cref="EndsInsideOpenQuotedField"/>.) For an unterminated input the reader
    /// must throw, and every prefix of the remaining lines must itself end inside a field.
    /// </summary>
    [Fact]
    public void ReadRecord_RecordBoundariesAgreeWithParseCsvRow_ForAllShortInputs()
    {
        const string alphabet = "a\",\n ";
        const int maxLength = 7;

        var stopwatch = Stopwatch.StartNew();
        int inputsChecked = 0;
        for (int length = 0; length <= maxLength; length++)
        {
            int total = 1;
            for (int k = 0; k < length; k++) total *= alphabet.Length;

            var chars = new char[length];
            for (int n = 0; n < total; n++)
            {
                int rest = n;
                for (int k = 0; k < length; k++)
                {
                    chars[k] = alphabet[rest % alphabet.Length];
                    rest /= alphabet.Length;
                }
                AssertReaderAgreesWithParseCsvRow(new string(chars));
                inputsChecked++;
            }
        }
        stopwatch.Stop();
        _output.WriteLine($"Checked {inputsChecked} inputs against ParseCsvRow in {stopwatch.ElapsedMilliseconds} ms");

        Assert.Equal(97656, inputsChecked);   // sum of 5^0 .. 5^7: guards against a silently shrunken search
    }

    [Fact]
    public void ReadRecord_AfterUnterminatedQuoteException_ReturnsNull()
    {
        var reader = new CsvRecordReader(new StringReader("\"open\nmore\n"));
        Assert.Throws<CsvFormatException>(() => reader.ReadRecord());

        Assert.Null(reader.ReadRecord());
    }

    [Fact]
    public void Constructor_NullReader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CsvRecordReader(null!));
    }

    [Fact]
    public void Constructor_FirstLineNumberBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CsvRecordReader(new StringReader("a"), 0));
    }

    /// <summary>
    /// Bug A regression at the reader level: a single, correctly terminated quoted field
    /// spanning 20,000 physical lines must be read in linear time (the old reader rescanned
    /// the whole growing text for every appended line).
    /// </summary>
    [Fact]
    public void ReadRecord_VeryLongMultiLineQuotedField_IsReadInLinearTime()
    {
        const int innerLines = 20_000;
        var sb = new StringBuilder("a,\"first\n");
        for (int i = 0; i < innerLines; i++)
            sb.Append("some more text in the same field\n");
        sb.Append("last\",b\nnext,row\n");

        var stopwatch = Stopwatch.StartNew();
        var records = ReadAll(sb.ToString());
        stopwatch.Stop();
        _output.WriteLine($"{innerLines}-line quoted field: {stopwatch.ElapsedMilliseconds} ms");

        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].LineNumber);
        Assert.Equal(innerLines + 3, records[1].LineNumber);
        Assert.True(stopwatch.ElapsedMilliseconds < LinearTimeCeilingMs,
            $"Reading took {stopwatch.ElapsedMilliseconds} ms (ceiling {LinearTimeCeilingMs} ms)");
    }
}
