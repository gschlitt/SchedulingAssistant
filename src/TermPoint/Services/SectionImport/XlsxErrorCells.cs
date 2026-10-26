#if !BROWSER
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace TermPoint.Services.SectionImport;

/// <summary>
/// Finds the cells of one worksheet that hold an Excel error, by looking at the workbook's XML directly.
///
/// <para>Why this exists: ExcelDataReader 3.9.0 reports an error cell through
/// <c>IExcelDataReader.GetCellError</c> only for the seven classic errors (<c>#N/A</c>, <c>#REF!</c>,
/// <c>#DIV/0!</c>, <c>#NAME?</c>, <c>#NULL!</c>, <c>#NUM!</c>, <c>#VALUE!</c>) and <c>#GETTING_DATA</c>. For any
/// other error (a newer one such as <c>#SPILL!</c> or <c>#CALC!</c>) it gives a null value and no error, which is
/// exactly what an empty cell looks like, so the cell would be imported as blank. A worksheet part marks such a
/// cell with the cell type <c>e</c>, so this class lists those cells and <see cref="ImportFileReader"/> treats the
/// ones the library lost as errors it cannot name.</para>
///
/// <para>It is purely additive and safe: it reads the zip and the sheet part with the framework's own readers,
/// never throws, and on anything unexpected (a missing part, odd markup) simply returns what it has found so far,
/// in which case the behaviour is the library's alone. Desktop only, like the reader.</para>
/// </summary>
internal static class XlsxErrorCells
{
    /// <summary>The part that lists the workbook's sheets.</summary>
    private const string WorkbookPart = "xl/workbook.xml";

    /// <summary>The part that maps each sheet's relationship id to the part that holds it.</summary>
    private const string WorkbookRelationshipsPart = "xl/_rels/workbook.xml.rels";

    /// <summary>The namespace of the <c>r:id</c> attribute that ties a sheet to its relationship.</summary>
    private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Lists the cells of the named worksheet whose cell type is error.
    /// </summary>
    /// <param name="content">The whole <c>.xlsx</c> file.</param>
    /// <param name="sheetName">The sheet's name; the first sheet whose name matches (ignoring case and spacing) is used.</param>
    /// <returns>
    /// The cells as (row, column): the row number Excel shows (the first row is 1) and the zero-based column index
    /// (column A is 0). Empty when there is none, the sheet is not found or the file cannot be read this way.
    /// </returns>
    public static HashSet<(int Row, int Column)> Find(byte[] content, string sheetName)
    {
        var found = new HashSet<(int Row, int Column)>();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            var sheetPart = FindSheetPart(zip, sheetName);
            var entry = sheetPart is null ? null : FindEntry(zip, sheetPart);
            if (entry is null)
                return found;

            using var stream = entry.Open();
            using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "c" && xml.GetAttribute("t") == "e"
                    && TryParseReference(xml.GetAttribute("r"), out var cell))
                    found.Add(cell);
            }
        }
        catch (Exception)
        {
            // Whatever was found before the problem stands; see the class comment.
        }
        return found;
    }

    /// <summary>
    /// Finds the name of the part that holds a sheet: the workbook part gives the sheet's relationship id and the
    /// relationships part says where that id points.
    /// </summary>
    /// <param name="zip">The open workbook package.</param>
    /// <param name="sheetName">The sheet's name.</param>
    /// <returns>The part's name inside the zip (for example <c>xl/worksheets/sheet1.xml</c>), or null when it cannot be found.</returns>
    private static string? FindSheetPart(ZipArchive zip, string sheetName)
    {
        var workbook = LoadPart(zip, WorkbookPart);
        var relationships = LoadPart(zip, WorkbookRelationshipsPart);
        if (workbook is null || relationships is null)
            return null;

        var relationshipId = workbook.Descendants()
            .Where(e => e.Name.LocalName == "sheet" && NameMatch.Equal((string?)e.Attribute("name") ?? string.Empty, sheetName))
            .Select(e => (string?)e.Attribute(RelationshipNamespace + "id"))
            .FirstOrDefault();
        if (relationshipId is null)
            return null;

        var target = relationships.Descendants()
            .Where(e => e.Name.LocalName == "Relationship" && (string?)e.Attribute("Id") == relationshipId)
            .Select(e => (string?)e.Attribute("Target"))
            .FirstOrDefault();
        return target is null ? null : ResolveTarget(target);
    }

    /// <summary>
    /// Turns a relationship target into a part name inside the zip: a target that starts with a slash is already
    /// from the package root; any other is relative to the <c>xl</c> folder, and <c>.</c> and <c>..</c> steps are applied.
    /// </summary>
    /// <param name="target">The relationship's <c>Target</c>.</param>
    /// <returns>The part name, without a leading slash.</returns>
    private static string ResolveTarget(string target)
    {
        var parts = new List<string>();
        var path = target.StartsWith('/') ? target : "xl/" + target;
        foreach (var step in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (step == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
            }
            else if (step != ".")
            {
                parts.Add(step);
            }
        }
        return string.Join('/', parts);
    }

    /// <summary>Finds a part by name, ignoring case (part names are not case-sensitive).</summary>
    /// <param name="zip">The open workbook package.</param>
    /// <param name="partName">The part's name.</param>
    /// <returns>The entry, or null.</returns>
    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string partName) =>
        zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, partName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads a small XML part (the workbook or its relationships).</summary>
    /// <param name="zip">The open workbook package.</param>
    /// <param name="partName">The part's name.</param>
    /// <returns>The document, or null when the part is missing.</returns>
    private static XElement? LoadPart(ZipArchive zip, string partName)
    {
        var entry = FindEntry(zip, partName);
        if (entry is null)
            return null;
        using var stream = entry.Open();
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XElement.Load(xml);
    }

    /// <summary>
    /// Reads a cell reference such as <c>B7</c> into its row and column.
    /// </summary>
    /// <param name="reference">The cell's <c>r</c> attribute; may be null.</param>
    /// <param name="cell">The row (1-based) and column (0-based, A is 0) when the reference is valid.</param>
    /// <returns>True when the reference was understood.</returns>
    private static bool TryParseReference(string? reference, out (int Row, int Column) cell)
    {
        cell = default;
        if (string.IsNullOrEmpty(reference))
            return false;

        int index = 0, column = 0;
        while (index < reference.Length && reference[index] is >= 'A' and <= 'Z')
            column = column * 26 + (reference[index++] - 'A' + 1);
        if (index == 0 || index == reference.Length
            || !int.TryParse(reference.AsSpan(index), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var row) || row < 1)
            return false;

        cell = (row, column - 1);
        return true;
    }
}
#endif
