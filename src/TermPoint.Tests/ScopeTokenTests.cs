using TermPoint.Behaviors;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="ScopeToken"/>, the run-once disposable behind
/// <see cref="ListScrollAnchorBehavior.Pause"/> and <see cref="SuppressPopupScrollBehavior.Bypass"/>.
/// </summary>
public class ScopeTokenTests
{
    /// <summary>The action runs on the first dispose, not before.</summary>
    [Fact]
    public void Dispose_RunsTheActionOnce()
    {
        int runs = 0;
        var token = new ScopeToken(() => runs++);
        Assert.Equal(0, runs);

        token.Dispose();
        token.Dispose();
        token.Dispose();

        Assert.Equal(1, runs);
    }

    /// <summary>A <c>using</c> block releases the scope at its end.</summary>
    [Fact]
    public void UsingBlock_RunsTheActionAtTheEnd()
    {
        int runs = 0;

        using (new ScopeToken(() => runs++))
            Assert.Equal(0, runs);

        Assert.Equal(1, runs);
    }

    /// <summary>The shared "nothing" token and a token without an action are harmless to dispose repeatedly.</summary>
    [Fact]
    public void NoneAndNullAction_AreHarmless()
    {
        ScopeToken.None.Dispose();
        ScopeToken.None.Dispose();
        new ScopeToken(null).Dispose();
    }

    /// <summary>If the action throws, the token still counts as released, so a retry cannot run it again.</summary>
    [Fact]
    public void ThrowingAction_IsNotRunAgain()
    {
        int runs = 0;
        var token = new ScopeToken(() => { runs++; throw new InvalidOperationException("boom"); });

        Assert.Throws<InvalidOperationException>(() => token.Dispose());
        token.Dispose(); // second call: nothing happens

        Assert.Equal(1, runs);
    }
}
