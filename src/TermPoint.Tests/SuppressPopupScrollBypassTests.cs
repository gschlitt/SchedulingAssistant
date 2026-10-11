using TermPoint.Behaviors;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the scoped bypass of <see cref="SuppressPopupScrollBehavior"/> (spec item 21,
/// "Virtualization re-test"). The routed-event handler needs a visual tree, so these check the
/// depth counter that decides whether the handler suppresses: it must rise and fall exactly with
/// open scopes, survive nesting, and be immune to a scope being disposed twice.
/// </summary>
public class SuppressPopupScrollBypassTests
{
    /// <summary>With no scope open the counter is zero, so suppression is in full effect.</summary>
    [Fact]
    public void NoScopeOpen_DepthIsZero()
    {
        Assert.Equal(0, SuppressPopupScrollBehavior.BypassDepth);
    }

    /// <summary>Opening a scope raises the counter; disposing it returns the counter to zero.</summary>
    [Fact]
    public void Bypass_RaisesDepthUntilDisposed()
    {
        var scope = SuppressPopupScrollBehavior.Bypass();
        Assert.Equal(1, SuppressPopupScrollBehavior.BypassDepth);

        scope.Dispose();
        Assert.Equal(0, SuppressPopupScrollBehavior.BypassDepth);
    }

    /// <summary>Scopes nest: suppression resumes only when the outermost scope is disposed.</summary>
    [Fact]
    public void NestedScopes_ResumeOnlyWhenOutermostEnds()
    {
        using (SuppressPopupScrollBehavior.Bypass())
        {
            using (SuppressPopupScrollBehavior.Bypass())
                Assert.Equal(2, SuppressPopupScrollBehavior.BypassDepth);

            Assert.Equal(1, SuppressPopupScrollBehavior.BypassDepth);
        }

        Assert.Equal(0, SuppressPopupScrollBehavior.BypassDepth);
    }

    /// <summary>
    /// Disposing one scope twice must not decrement twice, or it would cancel an enclosing
    /// scope that is still open.
    /// </summary>
    [Fact]
    public void DoubleDispose_DoesNotCancelAnOuterScope()
    {
        using var outer = SuppressPopupScrollBehavior.Bypass();
        var inner = SuppressPopupScrollBehavior.Bypass();

        inner.Dispose();
        inner.Dispose();

        Assert.Equal(1, SuppressPopupScrollBehavior.BypassDepth);
    }

    /// <summary>A scope disposed by a <c>using</c> block that exits through an exception still releases the counter.</summary>
    [Fact]
    public void ExceptionInsideScope_StillReleasesTheCounter()
    {
        // Typed as Action explicitly: a lambda that only throws would otherwise also fit the
        // (obsolete) async overload of Assert.Throws.
        Action throwsInsideScope = () =>
        {
            using (SuppressPopupScrollBehavior.Bypass())
                throw new InvalidOperationException("boom");
        };

        Assert.Throws<InvalidOperationException>(throwsInsideScope);

        Assert.Equal(0, SuppressPopupScrollBehavior.BypassDepth);
    }
}
