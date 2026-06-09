using System.IO;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs a test only when the real-data
/// integration database (<see cref="RoomAvailabilityIntegrationTests.SourceDbPath"/>)
/// exists on the current machine.
/// <para>
/// The database is a private, machine-local copy of real scheduling data that is
/// deliberately not committed to the repository. On any machine that lacks it —
/// including the GitHub Actions runner — the test is reported as <em>skipped</em>
/// rather than failed, so the suite stays green in CI. On a developer machine that
/// has the file, the test runs normally.
/// </para>
/// <para>
/// Because the <see cref="FactAttribute.Skip"/> reason is set during test discovery,
/// xUnit never instantiates the test class for a skipped test, so the fixture's
/// <c>File.Copy</c> of the source database is also bypassed.
/// </para>
/// </summary>
public sealed class FactRequiresLocalDbAttribute : FactAttribute
{
    public FactRequiresLocalDbAttribute()
    {
        if (!File.Exists(RoomAvailabilityIntegrationTests.SourceDbPath))
        {
            Skip = $"Real-data integration DB not found at " +
                   $"'{RoomAvailabilityIntegrationTests.SourceDbPath}'. " +
                   $"This test only runs on a machine that has that local file.";
        }
    }
}
