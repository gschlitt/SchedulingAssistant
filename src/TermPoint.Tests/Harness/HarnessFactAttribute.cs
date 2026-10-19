using Xunit;

namespace TermPoint.Tests.Harness;

/// <summary>
/// A <see cref="FactAttribute"/> for the multi-computer harness tests (spec item 9.3): the test runs only when the
/// environment variable <see cref="EnvironmentVariableName"/> is set to <c>1</c>. Without it the test is reported as
/// <em>skipped</em>, so the normal suite stays as fast and as unchanged as it was; the harness is run on demand
/// (and before each release) with <c>TERMPOINT_HARNESS=1</c>.
/// <para>
/// Modelled on <see cref="FactRequiresLocalDbAttribute"/>: the skip reason is decided when the tests are
/// discovered, so xUnit never even creates the test class for a skipped test.
/// </para>
/// </summary>
public sealed class HarnessFactAttribute : FactAttribute
{
    /// <summary>The environment variable that switches the harness tests on (set it to <c>1</c>).</summary>
    public const string EnvironmentVariableName = "TERMPOINT_HARNESS";

    /// <summary>Marks the test as skipped unless <see cref="EnvironmentVariableName"/> is <c>1</c>.</summary>
    public HarnessFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariableName) != "1")
        {
            Skip = $"Multi-computer harness test: set {EnvironmentVariableName}=1 to run it.";
        }
    }
}
