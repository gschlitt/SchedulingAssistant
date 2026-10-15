using TermPoint.Exceptions;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="SaveFailedException"/> (spec item 12): the named exception of a save-failure report must never
/// carry a path, however the original exception was worded.
/// </summary>
public sealed class SaveFailedExceptionTests
{
    /// <summary>Every step: fixed text plus the original's type name; no path, no inner exception, a stack trace.</summary>
    [Theory]
    [MemberData(nameof(AllSteps))]
    public void Exception_CarriesStepTextAndTypeName_ButNeverThePath(SaveStep step)
    {
        var original = new IOException(Path.Combine(Path.GetTempPath(), "secret-folder", "dept.db"));

        var failure = SaveFailedException.Create(step, original.GetType().Name);

        Assert.Equal(step, failure.Step);
        Assert.Equal(nameof(IOException), failure.OriginalType);
        Assert.Contains(SaveFailedException.MessageFor(step), failure.Message);
        Assert.Contains(nameof(IOException), failure.Message);
        Assert.DoesNotContain("secret-folder", failure.Message);
        Assert.DoesNotContain("dept.db", failure.Message);
        Assert.Null(failure.InnerException);
        Assert.NotNull(failure.StackTrace);
    }

    /// <summary>With no original exception the message is just the step's fixed text.</summary>
    [Fact]
    public void Exception_WithoutOriginal_IsJustTheFixedText()
    {
        var failure = SaveFailedException.Create(SaveStep.CrashProtectionMarker, null);

        Assert.Equal(SaveFailedException.MessageFor(SaveStep.CrashProtectionMarker), failure.Message);
        Assert.Null(failure.OriginalType);
    }

    /// <summary>Theory data: one row per <see cref="SaveStep"/> value.</summary>
    public static IEnumerable<object[]> AllSteps()
        => Enum.GetValues<SaveStep>().Select(step => new object[] { step });
}
