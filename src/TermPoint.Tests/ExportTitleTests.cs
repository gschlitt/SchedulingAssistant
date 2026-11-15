using TermPoint.ViewModels.GridView;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Collapse days, task 6: the PNG export's title banner text built by
/// <see cref="ScheduleGridViewModel.ComposeExportTitle"/> (unit, semester line, open days).
/// </summary>
public sealed class ExportTitleTests
{
    /// <summary>With nothing folded the title is the same as in 1.2.3: unit and semester line.</summary>
    [Fact]
    public void UnitAndSemester_NoCaption_Unchanged() =>
        Assert.Equal("UFV Biology — 2025-2026 — Fall",
            ScheduleGridViewModel.ComposeExportTitle("UFV Biology", "2025-2026 — Fall", ""));

    /// <summary>A caption is appended with the same separator.</summary>
    [Fact]
    public void WithCaption_AppendedAfterSemester() =>
        Assert.Equal("UFV Biology — 2025-2026 — Fall — Mon, Wed, Fri only",
            ScheduleGridViewModel.ComposeExportTitle("UFV Biology", "2025-2026 — Fall", "Mon, Wed, Fri only"));

    /// <summary>No unit name: the semester line leads, no stray separator (as before).</summary>
    [Fact]
    public void NoUnit_SemesterAndCaption() =>
        Assert.Equal("Fall — Tue, Thu only",
            ScheduleGridViewModel.ComposeExportTitle(null, "Fall", "Tue, Thu only"));

    /// <summary>An empty semester line is skipped rather than leaving a dangling separator.</summary>
    [Fact]
    public void EmptySemesterLine_Skipped()
    {
        Assert.Equal("UFV Biology — Fri only", ScheduleGridViewModel.ComposeExportTitle("UFV Biology", "", "Fri only"));
        Assert.Equal("UFV Biology", ScheduleGridViewModel.ComposeExportTitle("UFV Biology", "", ""));
    }

    /// <summary>Only a caption: the caption alone. Nothing at all: empty (no banner is drawn).</summary>
    [Fact]
    public void CaptionAlone_And_Nothing()
    {
        Assert.Equal("Mon only", ScheduleGridViewModel.ComposeExportTitle(" ", null, "Mon only"));
        Assert.Equal("", ScheduleGridViewModel.ComposeExportTitle(null, null, null));
    }
}
