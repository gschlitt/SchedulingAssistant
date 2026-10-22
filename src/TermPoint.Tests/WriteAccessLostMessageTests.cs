using System.Text;
using Bugsnag;
using TermPoint.Data;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// F4: one plain message per refused write (<see cref="WriteAccessLostException"/>), and nothing else. Pins the
/// three things around the exception itself: <see cref="WriteFailureText"/> (which text a catch block shows),
/// the loggers (<c>FileAppLogger</c> and <c>ConsoleAppLogger</c> log it locally only — no BugSnag report, no
/// developer-label banner, no development-time re-throw) and the banner helper for sites that show nothing inline.
/// </summary>
public sealed class WriteAccessLostMessageTests
{
    // ── The text ──────────────────────────────────────────────────────────────

    /// <summary>The sentence is exactly the one the user approved, and it is the exception's own message.</summary>
    [Fact]
    public void TheMessage_IsExactlyTheApprovedSentence()
    {
        Assert.Equal("You no longer have write access, so this change wasn't saved.", WriteAccessLostException.PlainMessage);
        Assert.Equal(WriteAccessLostException.PlainMessage, new WriteAccessLostException().Message);
    }

    /// <summary>The helper recognises the exception directly, wrapped in an inner exception, and inside an aggregate; nothing else.</summary>
    [Fact]
    public void IsWriteAccessLost_FindsItAnywhereInTheChain_AndOnlyThere()
    {
        var lost = new WriteAccessLostException();

        Assert.True(WriteFailureText.IsWriteAccessLost(lost));
        Assert.True(WriteFailureText.IsWriteAccessLost(new InvalidOperationException("wrapped", lost)));
        Assert.True(WriteFailureText.IsWriteAccessLost(new AggregateException(new IOException("other"), lost)));
        Assert.False(WriteFailureText.IsWriteAccessLost(new InvalidOperationException("ordinary")));
        Assert.False(WriteFailureText.IsWriteAccessLost(new AggregateException(new IOException("other"))));
        Assert.False(WriteFailureText.IsWriteAccessLost(null));
    }

    /// <summary>
    /// The inline text for a catch block: none for a refused write (the logger's banner carries the one plain message,
    /// because the views hide their inline error line while an editor is open), otherwise the site's own fallback.
    /// </summary>
    [Fact]
    public void For_GivesNoInlineTextForARefusedWrite_ElseTheFallback()
    {
        const string fallback = "The save could not be completed. Please try again.";

        Assert.Null(WriteFailureText.For(new WriteAccessLostException(), fallback));
        Assert.Null(WriteFailureText.For(new InvalidOperationException("wrapped", new WriteAccessLostException()), fallback));
        Assert.Equal(fallback, WriteFailureText.For(new InvalidOperationException("disk"), fallback));
        Assert.Equal(fallback, WriteFailureText.For(null, fallback));
    }

    /// <summary>
    /// The banner helper for sites with no inline text: one plain notice for a refused write, nothing for any other
    /// failure (those keep their own handling).
    /// </summary>
    [Fact]
    public void NotifyIfWriteAccessLost_ShowsOnePlainNotice_OnlyForARefusedWrite()
    {
        var logger = new RecordingAppLogger();

        Assert.True(WriteFailureText.NotifyIfWriteAccessLost(logger, new WriteAccessLostException()));
        Assert.False(WriteFailureText.NotifyIfWriteAccessLost(logger, new InvalidOperationException("disk")));

        Assert.Equal(new[] { WriteAccessLostException.PlainMessage }, logger.UserNotices);
        Assert.Empty(logger.Errors);
    }

    // ── FileAppLogger ─────────────────────────────────────────────────────────

    /// <summary>A BugSnag delivery that keeps each payload instead of sending it anywhere.</summary>
    private sealed class CapturingDelivery : IDelivery
    {
        /// <summary>The JSON of every payload "sent", in order.</summary>
        public List<string> Payloads { get; } = new();

        /// <inheritdoc/>
        public void Send(IPayload payload) => Payloads.Add(Encoding.UTF8.GetString(payload.Serialize()));
    }

    /// <summary>Builds a <see cref="FileAppLogger"/> wired to a real BugSnag client whose delivery is captured.</summary>
    /// <param name="throwOnError">The logger's development-time re-throw switch.</param>
    private static (FileAppLogger Logger, CapturingDelivery Delivery, List<string> Banners) NewFileLogger(bool throwOnError)
    {
        var delivery = new CapturingDelivery();
        var config = new Configuration("0123456789abcdef0123456789abcdef")
        {
            ReleaseStage        = "production",
            NotifyReleaseStages = new[] { "production" },
            AppVersion          = "1.2.3",
            AutoCaptureSessions = false,
            ProjectNamespaces   = new[] { "TermPoint" }
        };
        var logger = new FileAppLogger { BugsnagClient = new Bugsnag.Client(config, delivery), ThrowOnError = throwOnError };
        var banners = new List<string>();
        logger.ErrorLogged += (_, message) => banners.Add(message);
        return (logger, delivery, banners);
    }

    /// <summary>
    /// A refused read-only write is logged locally and shown as exactly one banner carrying the plain sentence (never
    /// the developer label): no BugSnag report, and no re-throw even with the development switch on — directly or
    /// wrapped.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FileAppLogger_LogError_ForARefusedWrite_ShowsOnePlainBanner_ReportsNothing_DoesNotThrow(bool throwOnError, bool wrapped)
    {
        var (logger, delivery, banners) = NewFileLogger(throwOnError);
        Exception ex = new WriteAccessLostException();
        if (wrapped) ex = new InvalidOperationException("wrapper", ex);

        var thrown = Record.Exception(() => logger.LogError(ex, "SubjectListViewModel.Add"));

        Assert.Null(thrown);
        Assert.Empty(delivery.Payloads);
        Assert.Equal(new[] { WriteAccessLostException.PlainMessage }, banners);
    }

    /// <summary>
    /// Control for the test above: any OTHER exception is still reported to BugSnag and raises the banner, so the
    /// silence above is specific to the refused write.
    /// </summary>
    [Fact]
    public void FileAppLogger_LogError_ForAnyOtherException_StillReportsAndRaisesTheBanner()
    {
        var (logger, delivery, banners) = NewFileLogger(throwOnError: false);

        logger.LogError(new InvalidOperationException("boom"), "SubjectListViewModel.Add");

        Assert.Single(delivery.Payloads);
        Assert.Equal(new[] { "SubjectListViewModel.Add" }, banners);
    }

    // ── ConsoleAppLogger (the browser build's logger) ─────────────────────────

    /// <summary>
    /// <c>ConsoleAppLogger</c> follows the same rule: a refused write is not re-thrown even with the development
    /// switch on, while any other exception still is.
    /// </summary>
    [Fact]
    public void ConsoleAppLogger_LogError_ForARefusedWrite_DoesNotThrow_ButOtherExceptionsStillDo()
    {
        var logger = new ConsoleAppLogger { ThrowOnError = true };

        Assert.Null(Record.Exception(() => logger.LogError(new WriteAccessLostException(), "ctx")));
        Assert.Null(Record.Exception(() =>
            logger.LogError(new InvalidOperationException("wrapper", new WriteAccessLostException()), "ctx")));
        Assert.IsType<InvalidOperationException>(
            Record.Exception(() => logger.LogError(new InvalidOperationException("boom"), "ctx")));
    }
}
