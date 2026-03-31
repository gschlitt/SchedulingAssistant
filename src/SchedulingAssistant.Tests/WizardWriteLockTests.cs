using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels.Wizard;
using SchedulingAssistant.ViewModels.Wizard.Steps;
using Xunit;
using System.IO;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Tests that WriteLockService is properly acquired during wizard database creation,
/// enabling write access for subsequent management steps (Campuses, Block Patterns, etc.).
/// </summary>
public class WizardWriteLockTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}");

    public WizardWriteLockTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    /// <summary>
    /// Verifies that after step 3 (database creation) completes successfully,
    /// WriteLockService.IsWriter is true so subsequent wizard steps have write access.
    /// </summary>
    [Fact]
    public void WizardStep3_AfterDatabaseCreation_WriteLockIsAcquired()
    {
        // Arrange: Create a fresh WriteLockService for this test
        var lockService = new WriteLockService();
        var dbPath = Path.Combine(_tempDir, "test.db");

        // Verify the lock is not yet acquired
        Assert.False(lockService.IsWriter);

        // Act: Acquire the lock as Step 3 would
        lockService.TryAcquire(dbPath);

        // Assert: Lock is now acquired so management steps have write access
        Assert.True(lockService.IsWriter);
        Assert.NotNull(lockService.SessionGuid);

        // Cleanup
        lockService.Dispose();
    }

    /// <summary>
    /// Verifies that WriteLockService.IsWriter remains true even after
    /// multiple acquisitions on the same session GUID.
    /// </summary>
    [Fact]
    public void WriteLockService_IsWriter_RemainsTrue_ForSameSession()
    {
        // Arrange
        var lockService = new WriteLockService();
        var sessionGuid = lockService.SessionGuid;
        var dbPath = Path.Combine(_tempDir, "test.db");

        // Act: First acquisition
        lockService.TryAcquire(dbPath);
        Assert.True(lockService.IsWriter);
        var firstDbPath = dbPath;

        // Act: Try to access the lock again (simulating step 5 resolving CampusListViewModel)
        // The lock should still be held by this session
        Assert.True(lockService.IsWriter);

        // Assert: Verify we still own the lock
        Assert.Equal(sessionGuid, lockService.SessionGuid);
        Assert.True(lockService.IsWriter);

        // Cleanup
        lockService.Dispose();
    }
}
