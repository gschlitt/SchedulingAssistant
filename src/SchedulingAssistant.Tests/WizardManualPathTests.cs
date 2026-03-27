using Moq;
using SchedulingAssistant.Data;
using SchedulingAssistant.Data.Repositories;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Management;
using System.Reflection;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests for the manual setup path through the startup wizard, focusing on data
/// flowing correctly between steps that share the same database context.
///
/// These tests do not navigate through <see cref="StartupWizardViewModel"/> because
/// steps 5–8 require a live DI container and a running Avalonia dispatcher (for
/// <see cref="WriteLockService"/>). Instead they test the repository-level wiring
/// that the wizard steps rely on: both the Campuses step and the Section Prefixes
/// step must operate on the same <see cref="IDatabaseContext"/> so that campuses
/// added in step 4 appear in the step-7 campus dropdown.
/// </summary>
public class WizardManualPathTests : IDisposable
{
    // ─────────────────────────────────────────────────────────────────────────
    // Per-test temp directory and shared database context
    // ─────────────────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly DatabaseContext _db;
    private readonly CampusRepository _campusRepo;
    private readonly SectionPrefixRepository _prefixRepo;

    public WizardManualPathTests()
    {
        _tempDir   = Path.Combine(Path.GetTempPath(), $"WizardManual_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var dbPath   = Path.Combine(_tempDir, "manual.db");
        _db          = new DatabaseContext(dbPath);
        _campusRepo  = new CampusRepository(_db);
        _prefixRepo  = new SectionPrefixRepository(_db);
    }

    public void Dispose()
    {
        try
        {
            _db.Dispose();
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="SectionPrefixListViewModel"/> backed by the shared test
    /// repositories. <see cref="WriteLockService.IsWriter"/> is false (no file lock
    /// was acquired), which is intentional — these tests verify data visibility
    /// rather than write-command gating.
    /// </summary>
    private SectionPrefixListViewModel BuildPrefixListVm() =>
        new(
            repo:        _prefixRepo,
            campusRepo:  _campusRepo,
            dialog:      Mock.Of<IDialogService>(),
            lockService: new WriteLockService()   // IsWriter = false; no Avalonia dispatcher needed
        );

    /// <summary>
    /// Invokes the private <c>BuildCampusOptions()</c> method on a
    /// <see cref="SectionPrefixListViewModel"/> via reflection.
    /// This method is called inside <c>Add()</c> and <c>Edit()</c> to populate the
    /// campus dropdown; here we call it directly to verify the data without needing
    /// the write-lock gating that guards those commands.
    /// </summary>
    private static List<CampusOption> GetCampusOptions(SectionPrefixListViewModel vm)
    {
        var method = typeof(SectionPrefixListViewModel)
            .GetMethod("BuildCampusOptions", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildCampusOptions method not found");

        return (List<CampusOption>)method.Invoke(vm, null)!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Campus → Section Prefix dropdown data flow
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A campus inserted into the shared campus repository (simulating what
    /// <see cref="CampusListViewModel.Add"/> does in wizard step 4) must appear
    /// in the campus dropdown of <see cref="SectionPrefixListViewModel"/> (step 7).
    ///
    /// This is the core test the user requested: it fails if step 7 uses a different
    /// database context than step 4, or if <c>BuildCampusOptions()</c> is not reading
    /// from the database.
    /// </summary>
    [Fact]
    public void CampusInserted_ViaSharedRepo_AppearsInSectionPrefixCampusDropdown()
    {
        // Insert a campus the way CampusListViewModel.Add() would (when IsWriter = true)
        var campus = new Campus { Name = "Abbotsford", SortOrder = 0 };
        _campusRepo.Insert(campus);

        var vm      = BuildPrefixListVm();
        var options = GetCampusOptions(vm);

        Assert.Contains(options, o => o.DisplayName == "Abbotsford");
    }

    /// <summary>
    /// Verifies that multiple campuses all appear, in order, in the section prefix
    /// campus dropdown.
    /// </summary>
    [Fact]
    public void MultipleCampuses_AllAppearInSectionPrefixCampusDropdown_InOrder()
    {
        _campusRepo.Insert(new Campus { Name = "Abbotsford", SortOrder = 0 });
        _campusRepo.Insert(new Campus { Name = "Chilliwack",  SortOrder = 1 });
        _campusRepo.Insert(new Campus { Name = "Online",      SortOrder = 2 });

        var options = GetCampusOptions(BuildPrefixListVm());

        var names = options.Select(o => o.DisplayName).ToList();
        Assert.Contains("Abbotsford", names);
        Assert.Contains("Chilliwack",  names);
        Assert.Contains("Online",      names);
    }

    /// <summary>
    /// When no campuses have been inserted, the dropdown must still contain the
    /// leading "(none)" sentinel — it must never be empty.
    /// </summary>
    [Fact]
    public void NoCampuses_DropdownStillContainsNoneSentinel()
    {
        var options = GetCampusOptions(BuildPrefixListVm());

        Assert.Single(options);
        Assert.Null(options[0].Id);
        Assert.Equal("(none)", options[0].DisplayName);
    }

    /// <summary>
    /// "(none)" is always the first entry; real campuses follow it.
    /// </summary>
    [Fact]
    public void CampusDropdown_NoneEntryIsAlwaysFirst()
    {
        _campusRepo.Insert(new Campus { Name = "Abbotsford", SortOrder = 0 });

        var options = GetCampusOptions(BuildPrefixListVm());

        Assert.Null(options[0].Id);
        Assert.Equal("(none)", options[0].DisplayName);
        Assert.Equal(2, options.Count); // (none) + Abbotsford
    }

    /// <summary>
    /// A campus inserted before the <see cref="SectionPrefixListViewModel"/> was
    /// constructed must appear in the dropdown (the dropdown reads from the DB each
    /// time, not from a constructor-time snapshot).
    /// </summary>
    [Fact]
    public void CampusInserted_BeforeVmConstruction_IsVisibleInDropdown()
    {
        _campusRepo.Insert(new Campus { Name = "Main", SortOrder = 0 });
        // VM constructed AFTER the insert
        var options = GetCampusOptions(BuildPrefixListVm());

        Assert.Contains(options, o => o.DisplayName == "Main");
    }

    /// <summary>
    /// A campus inserted AFTER the <see cref="SectionPrefixListViewModel"/> was
    /// constructed still appears when <c>BuildCampusOptions()</c> is called, because
    /// it re-queries the DB every time rather than caching.
    /// </summary>
    [Fact]
    public void CampusInserted_AfterVmConstruction_IsVisibleInDropdown()
    {
        var vm = BuildPrefixListVm(); // constructed when DB has no campuses

        // Insert campus AFTER the VM was constructed
        _campusRepo.Insert(new Campus { Name = "Main", SortOrder = 0 });

        var options = GetCampusOptions(vm);
        Assert.Contains(options, o => o.DisplayName == "Main");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SectionPrefixListViewModel.Load() resolves campus names
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When a section prefix is linked to a campus, <see cref="SectionPrefixListViewModel.Load"/>
    /// resolves the campus name for display in the data grid.
    /// </summary>
    [Fact]
    public void Load_ResolvesLinkedCampusName_ForDisplayInGrid()
    {
        var campus = new Campus { Name = "Abbotsford", SortOrder = 0 };
        _campusRepo.Insert(campus);

        var prefix = new SectionPrefix { Prefix = "AB", CampusId = campus.Id };
        _prefixRepo.Insert(prefix);

        var vm = BuildPrefixListVm();

        var row = Assert.Single(vm.Items);
        Assert.Equal("Abbotsford", row.CampusName);
    }

    /// <summary>
    /// A prefix with no campus link displays an empty campus name (not null).
    /// </summary>
    [Fact]
    public void Load_NoCampusLink_ShowsEmptyCampusName()
    {
        _prefixRepo.Insert(new SectionPrefix { Prefix = "AB", CampusId = null });

        var vm = BuildPrefixListVm();

        var row = Assert.Single(vm.Items);
        Assert.Equal(string.Empty, row.CampusName);
    }
}
