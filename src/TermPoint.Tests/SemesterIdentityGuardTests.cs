using TermPoint.Services;
using TermPoint.ViewModels.Management;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the shared-schedule semester guard
/// (<see cref="SharingViewModel.CheckSemesterIdentity"/>).
///
/// <para>Imported sections are stamped with the ACTIVE semester's ID regardless of which
/// semester the file was exported from, so a cross-semester import does not merely mislead —
/// it mis-attributes the data. The guard therefore refuses outright rather than warning.</para>
///
/// <para>Semester names are bare ("Fall", "Winter") with the year held separately, so the
/// academic year is the field that separates Fall 2026 from Fall 2027. The rule is "enforce
/// when we can, warn when we can't": a mismatch we can establish is refused; a file lacking
/// the identity to check is allowed with a visible warning.</para>
/// </summary>
public class SemesterIdentityGuardTests
{
    /// <summary>Builds a parse result carrying only the header identity fields under test.</summary>
    private static ImportResult ResultFor(string? semesterName, string? academicYearName = null,
                                          string? semesterId = null)
        => new(Set: null, TotalRows: 0, SkippedRows: 0, Warnings: new(), FileError: null,
               SemesterName: semesterName, AcademicYearName: academicYearName,
               SemesterId: semesterId);

    // ── Refusals ──────────────────────────────────────────────────────────────

    [Fact]
    public void DifferentSemesterName_IsRefused()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Winter", "2026-2027"), "Fall", "2026-2027");

        Assert.NotNull(refusal);
        Assert.Null(warning);
        Assert.Contains("Winter", refusal);
        Assert.Contains("Fall", refusal);
    }

    /// <summary>
    /// The case the old name-only check could never catch: same semester name, different year.
    /// "Fall" == "Fall" compared equal, so a Fall 2026 share imported while Fall 2027 was open
    /// raised no warning at all and was silently restamped.
    /// </summary>
    [Fact]
    public void SameSemesterNameDifferentAcademicYear_IsRefused()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Fall", "2026-2027"), "Fall", "2027-2028");

        Assert.NotNull(refusal);
        Assert.Null(warning);
        Assert.Contains("2026-2027", refusal);
        Assert.Contains("2027-2028", refusal);
    }

    /// <summary>The refusal must tell the user how to proceed, not just that it failed.</summary>
    [Fact]
    public void Refusal_ExplainsHowToProceed()
    {
        var (refusal, _) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Winter", "2025-2026"), "Fall", "2027-2028");

        Assert.NotNull(refusal);
        Assert.Contains("Switch to", refusal);
        Assert.Contains("Winter 2025-2026", refusal);
        Assert.Contains("Fall 2027-2028", refusal);
    }

    // ── Matches ───────────────────────────────────────────────────────────────

    [Fact]
    public void MatchingSemesterAndYear_PassesCleanly()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Fall", "2026-2027"), "Fall", "2026-2027");

        Assert.Null(refusal);
        Assert.Null(warning);
    }

    [Fact]
    public void Comparison_IgnoresCaseAndSurroundingWhitespace()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("  fall  ", " 2026-2027 "), "Fall", "2026-2027");

        Assert.Null(refusal);
        Assert.Null(warning);
    }

    // ── Unverifiable files: allow with a warning ──────────────────────────────

    [Fact]
    public void NoSemesterInFile_IsAllowedWithWarning()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor(null), "Fall", "2026-2027");

        Assert.Null(refusal);
        Assert.NotNull(warning);
    }

    /// <summary>
    /// Files predating the academic-year header match on name but cannot be confirmed to be the
    /// same year. These still import — refusing them would break every share already in
    /// circulation — but the uncertainty is surfaced.
    /// </summary>
    [Fact]
    public void SemesterMatchesButFileHasNoAcademicYear_IsAllowedWithWarning()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Fall", academicYearName: null), "Fall", "2026-2027");

        Assert.Null(refusal);
        Assert.NotNull(warning);
        Assert.Contains("academic year", warning);
    }

    [Fact]
    public void NoActiveAcademicYear_CannotVerifyYear_IsAllowedWithWarning()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Fall", "2026-2027"), "Fall", activeAcademicYearName: null);

        Assert.Null(refusal);
        Assert.NotNull(warning);
    }

    /// <summary>
    /// Import is gated on a single selected semester in the UI, so with nothing open there is
    /// nothing to compare against and nothing to protect.
    /// </summary>
    [Fact]
    public void NoActiveSemester_PassesWithoutComment()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Fall", "2026-2027"), activeSemesterName: null, activeAcademicYearName: null);

        Assert.Null(refusal);
        Assert.Null(warning);
    }

    /// <summary>
    /// The file's semester ID belongs to the sending database and is meaningless here, so it
    /// must never influence the decision.
    /// </summary>
    [Fact]
    public void ForeignSemesterId_DoesNotAffectOutcome()
    {
        var (refusal, warning) = SharingViewModel.CheckSemesterIdentity(
            ResultFor("Fall", "2026-2027", semesterId: "some-other-db-guid"), "Fall", "2026-2027");

        Assert.Null(refusal);
        Assert.Null(warning);
    }
}
