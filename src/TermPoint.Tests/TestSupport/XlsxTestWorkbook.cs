using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TermPoint.Tests.TestSupport;

/// <summary>
/// Builds a minimal, valid <c>.xlsx</c> workbook in memory (a zip of the smallest set of OpenXML parts
/// Excel and ExcelDataReader accept), so reader tests need no binary fixtures and no external tools.
///
/// <para>Usage: <c>new XlsxTestWorkbook().Sheet("Sections", s =&gt; s.Row("A", 1.5)).ToBytes()</c>.</para>
///
/// <para>Cell values: <c>null</c> leaves the cell out; <c>string</c> becomes an inline string; numbers
/// become numeric cells; <c>bool</c> a boolean cell; <see cref="TimeSpan"/> a numeric cell (its fraction
/// of a day) in the <c>h:mm</c> time style, which readers surface as a <see cref="DateTime"/>; and
/// <see cref="TimeFraction"/> does the same for an exact fraction, to build Excel's imprecise times.</para>
/// </summary>
public sealed class XlsxTestWorkbook
{
    /// <summary>The worksheet part and relationship namespaces used in the generated XML.</summary>
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Style index of the general (no special format) cell style.</summary>
    private const int GeneralStyle = 0;

    /// <summary>Style index of the <c>h:mm</c> time style (built-in number format 20).</summary>
    private const int TimeStyle = 1;

    private readonly List<(string Name, SheetBuilder Builder)> _sheets = new();

    /// <summary>A numeric time-of-day cell holding exactly this fraction of a day (0.5 is 12:00).</summary>
    /// <param name="Fraction">The fraction of a day.</param>
    public readonly record struct TimeFraction(double Fraction);

    /// <summary>Collects the rows of one worksheet.</summary>
    public sealed class SheetBuilder
    {
        internal readonly List<(int RowNumber, object?[] Cells)> Rows = new();

        /// <summary>Adds a row at an explicit row number (rows may skip numbers, leaving gaps).</summary>
        /// <param name="rowNumber">The 1-based row number Excel will show.</param>
        /// <param name="cells">The cells from column A; <c>null</c> leaves a cell out.</param>
        /// <returns>This builder.</returns>
        public SheetBuilder RowAt(int rowNumber, params object?[] cells)
        {
            Rows.Add((rowNumber, cells));
            return this;
        }

        /// <summary>Adds a row on the row after the previous one (row 1 for the first).</summary>
        /// <param name="cells">The cells from column A; <c>null</c> leaves a cell out.</param>
        /// <returns>This builder.</returns>
        public SheetBuilder Row(params object?[] cells) =>
            RowAt(Rows.Count == 0 ? 1 : Rows[^1].RowNumber + 1, cells);
    }

    /// <summary>Adds a worksheet.</summary>
    /// <param name="name">The sheet's tab name.</param>
    /// <param name="build">Adds the sheet's rows.</param>
    /// <returns>This workbook.</returns>
    public XlsxTestWorkbook Sheet(string name, Action<SheetBuilder> build)
    {
        var builder = new SheetBuilder();
        build(builder);
        _sheets.Add((name, builder));
        return this;
    }

    /// <summary>Writes the workbook.</summary>
    /// <returns>The bytes of the <c>.xlsx</c> file.</returns>
    public byte[] ToBytes()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddPart(zip, "[Content_Types].xml", ContentTypes());
            AddPart(zip, "_rels/.rels", RootRelationships());
            AddPart(zip, "xl/workbook.xml", WorkbookXml());
            AddPart(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships());
            AddPart(zip, "xl/styles.xml", StylesXml());
            for (int i = 0; i < _sheets.Count; i++)
                AddPart(zip, $"xl/worksheets/sheet{i + 1}.xml", WorksheetXml(_sheets[i].Builder));
        }
        return buffer.ToArray();
    }

    /// <summary>Adds one XML part to the zip.</summary>
    private static void AddPart(ZipArchive zip, string path, XDocument xml)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        xml.Save(writer);
    }

    /// <summary>[Content_Types].xml: declares the type of each part.</summary>
    private XDocument ContentTypes()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = new XElement(ns + "Types",
            new XElement(ns + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ns + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        for (int i = 0; i < _sheets.Count; i++)
            root.Add(new XElement(ns + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        return new XDocument(root);
    }

    /// <summary>_rels/.rels: points the package at the workbook part.</summary>
    private static XDocument RootRelationships() => new(
        new XElement(PackageRel + "Relationships",
            new XElement(PackageRel + "Relationship", new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml"))));

    /// <summary>xl/workbook.xml: lists the sheets by name.</summary>
    private XDocument WorkbookXml()
    {
        var sheets = new XElement(Main + "sheets");
        for (int i = 0; i < _sheets.Count; i++)
            sheets.Add(new XElement(Main + "sheet", new XAttribute("name", _sheets[i].Name),
                new XAttribute("sheetId", i + 1), new XAttribute(Rel + "id", $"rId{i + 1}")));
        return new XDocument(new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel.NamespaceName), sheets));
    }

    /// <summary>xl/_rels/workbook.xml.rels: maps each sheet's id to its part (the styles part follows the sheets).</summary>
    private XDocument WorkbookRelationships()
    {
        var root = new XElement(PackageRel + "Relationships");
        for (int i = 0; i < _sheets.Count; i++)
            root.Add(new XElement(PackageRel + "Relationship", new XAttribute("Id", $"rId{i + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i + 1}.xml")));
        root.Add(new XElement(PackageRel + "Relationship", new XAttribute("Id", $"rId{_sheets.Count + 1}"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));
        return new XDocument(root);
    }

    /// <summary>xl/styles.xml: two cell styles, general (0) and <c>h:mm</c> time (1, built-in number format 20).</summary>
    private static XDocument StylesXml() => new(
        new XElement(Main + "styleSheet",
            new XElement(Main + "fonts", new XAttribute("count", 1), new XElement(Main + "font")),
            new XElement(Main + "fills", new XAttribute("count", 2),
                new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "gray125")))),
            new XElement(Main + "borders", new XAttribute("count", 1), new XElement(Main + "border")),
            new XElement(Main + "cellStyleXfs", new XAttribute("count", 1), new XElement(Main + "xf")),
            new XElement(Main + "cellXfs", new XAttribute("count", 2),
                new XElement(Main + "xf", new XAttribute("numFmtId", 0)),
                new XElement(Main + "xf", new XAttribute("numFmtId", 20), new XAttribute("applyNumberFormat", 1)))));

    /// <summary>xl/worksheets/sheetN.xml: the sheet's rows, each with the explicit row number it was given.</summary>
    private static XDocument WorksheetXml(SheetBuilder sheet)
    {
        var data = new XElement(Main + "sheetData");
        foreach (var (rowNumber, cells) in sheet.Rows)
        {
            var row = new XElement(Main + "row", new XAttribute("r", rowNumber));
            for (int c = 0; c < cells.Length; c++)
            {
                var cell = CellXml(cells[c], $"{ColumnLetters(c)}{rowNumber}");
                if (cell is not null)
                    row.Add(cell);
            }
            data.Add(row);
        }
        return new XDocument(new XElement(Main + "worksheet", data));
    }

    /// <summary>Builds one <c>&lt;c&gt;</c> element, or null when the value is null (the cell is left out).</summary>
    private static XElement? CellXml(object? value, string reference)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                // Inline strings avoid a shared-strings part; xml:space keeps leading and trailing spaces.
                return new XElement(Main + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"),
                    new XElement(Main + "is",
                        new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            case bool flag:
                return new XElement(Main + "c", new XAttribute("r", reference), new XAttribute("t", "b"),
                    new XElement(Main + "v", flag ? "1" : "0"));
            case TimeSpan time:
                return NumberCell(reference, time.TotalDays, TimeStyle);
            case TimeFraction fraction:
                return NumberCell(reference, fraction.Fraction, TimeStyle);
            case IConvertible number:
                return NumberCell(reference, Convert.ToDouble(number, System.Globalization.CultureInfo.InvariantCulture), GeneralStyle);
            default:
                throw new ArgumentException($"Unsupported test cell type {value.GetType()}.");
        }
    }

    /// <summary>Builds a numeric cell with the given style index; the number is written with round-trip precision.</summary>
    private static XElement NumberCell(string reference, double number, int style) =>
        new(Main + "c", new XAttribute("r", reference), new XAttribute("s", style),
            new XElement(Main + "v", number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Converts a 0-based column index to Excel's column letters (0 is A, 25 is Z, 26 is AA).</summary>
    private static string ColumnLetters(int index)
    {
        var letters = new StringBuilder();
        for (int n = index; n >= 0; n = n / 26 - 1)
            letters.Insert(0, (char)('A' + n % 26));
        return letters.ToString();
    }
}
