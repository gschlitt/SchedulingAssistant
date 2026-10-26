using TermPoint.Services.SectionImport;
using TermPoint.Tests.TestSupport;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="XlsxErrorCells"/>, which finds the error cells of a worksheet from the workbook's own XML
/// so that an error ExcelDataReader has no name for (it reads such a cell as empty) is not lost. Workbooks are built
/// in memory by <see cref="XlsxTestWorkbook"/>.
/// </summary>
public class XlsxErrorCellsTests
{
    /// <summary>The cells holding errors are listed by Excel's row number and zero-based column, whatever the error.</summary>
    [Fact]
    public void Find_ListsEveryErrorCell_ByRowAndColumn()
    {
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s => s
            .Row("a", "b", "c")
            .Row("x", new XlsxTestWorkbook.ErrorCell("#N/A"), "y")
            .Row(new XlsxTestWorkbook.ErrorCell("#SPILL!"), null, new XlsxTestWorkbook.ErrorCell("#CALC!"))).ToBytes();

        var found = XlsxErrorCells.Find(bytes, "Sections");

        Assert.Equal(new HashSet<(int, int)> { (2, 1), (3, 0), (3, 2) }, found);
    }

    /// <summary>Columns past Z are lettered AA, AB...: column AA is index 26.</summary>
    [Fact]
    public void Find_UnderstandsColumnsPastZ()
    {
        var cells = new object?[28];
        cells[26] = new XlsxTestWorkbook.ErrorCell("#REF!");
        cells[27] = new XlsxTestWorkbook.ErrorCell("#REF!");
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s => s.Row(cells)).ToBytes();

        Assert.Equal(new HashSet<(int, int)> { (1, 26), (1, 27) }, XlsxErrorCells.Find(bytes, "Sections"));
    }

    /// <summary>Only the named sheet is looked at, wherever it sits in the workbook and however its name is cased.</summary>
    [Fact]
    public void Find_LooksOnlyAtTheNamedSheet()
    {
        var bytes = new XlsxTestWorkbook()
            .Sheet("Notes", s => s.Row(new XlsxTestWorkbook.ErrorCell("#N/A")))
            .Sheet("Sections", s => s.Row("a", "b").Row("c", new XlsxTestWorkbook.ErrorCell("#SPILL!")))
            .Sheet("Other", s => s.Row(null, null, new XlsxTestWorkbook.ErrorCell("#N/A"))).ToBytes();

        Assert.Equal(new HashSet<(int, int)> { (2, 1) }, XlsxErrorCells.Find(bytes, "sections"));
    }

    /// <summary>A sheet with no error cell gives an empty set, and so does a name that is not in the workbook.</summary>
    [Fact]
    public void Find_NoErrorsOrNoSuchSheet_IsEmpty()
    {
        var bytes = new XlsxTestWorkbook().Sheet("Sections", s => s.Row("a", 1.5, true)).ToBytes();

        Assert.Empty(XlsxErrorCells.Find(bytes, "Sections"));
        Assert.Empty(XlsxErrorCells.Find(bytes, "Missing"));
    }

    /// <summary>Bytes that are not a workbook give an empty set and no exception: the reader's own message covers them.</summary>
    [Fact]
    public void Find_NotAWorkbook_IsEmptyAndDoesNotThrow()
    {
        Assert.Empty(XlsxErrorCells.Find(Array.Empty<byte>(), "Sections"));
        Assert.Empty(XlsxErrorCells.Find(new byte[] { 1, 2, 3, 4, 5 }, "Sections"));
        var random = new byte[3000];
        new Random(7).NextBytes(random);
        Assert.Empty(XlsxErrorCells.Find(random, "Sections"));
    }
}
