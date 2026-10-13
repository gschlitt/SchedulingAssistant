using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs a test only on Windows; elsewhere it is reported as
/// <em>skipped</em>, never failed.
///
/// <para>Used by the "database held open by another program" tests. On Windows,
/// <c>FileShare.None</c> is enforced by the OS: any other open of the file (SQLite's included)
/// fails with a sharing violation, which is exactly the real-world condition under test
/// (a backup or sync tool holding the database). On macOS/Linux .NET emulates <c>FileShare</c>
/// with advisory locks that SQLite (and most other programs) ignore, so the same setup would not
/// block SQLite and the test would prove nothing.</para>
///
/// <para>As with <see cref="FactRequiresLocalDbAttribute"/>, the skip reason is set at discovery,
/// so the test class is not instantiated for a skipped test. CI runs <c>windows-latest</c>, so
/// these tests do run there.</para>
/// </summary>
public sealed class FactWindowsOnlyAttribute : FactAttribute
{
    /// <summary>Marks the test skipped when the current OS is not Windows.</summary>
    public FactWindowsOnlyAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only: needs OS-enforced FileShare.None (a real sharing violation).";
        }
    }
}
