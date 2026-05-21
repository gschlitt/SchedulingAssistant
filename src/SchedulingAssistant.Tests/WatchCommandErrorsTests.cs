using CommunityToolkit.Mvvm.Input;
using SchedulingAssistant.Services;
using SchedulingAssistant.ViewModels;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="ViewModelBase.WatchCommandErrors"/>.
///
/// <para>Uses a minimal concrete subclass (<see cref="TestViewModel"/>) that exposes
/// an <see cref="AsyncRelayCommand"/> wired through <c>WatchCommandErrors</c>.
/// The tests verify that faulted commands surface their exception via
/// <see cref="ViewModelBase.LastErrorMessage"/> and <see cref="App.Logger"/>,
/// while successful commands leave the error state untouched.</para>
///
/// <para><c>AsyncRelayCommand.ExecuteAsync</c> stores the faulted task in
/// <c>ExecutionTask</c> (triggering <c>PropertyChanged</c> and thus
/// <c>WatchCommandErrors</c>) and then re-throws the exception. The faulting
/// tests therefore catch the expected re-throw before asserting side effects.</para>
/// </summary>
public class WatchCommandErrorsTests : IDisposable
{
    private readonly IAppLogger _originalLogger;

    public WatchCommandErrorsTests()
    {
        // Swap App.Logger to a stub so WatchCommandErrors can call LogError without
        // touching the file system or Bugsnag. Restore in Dispose().
        _originalLogger = App.Logger;
    }

    public void Dispose()
    {
        // Restore the original logger to avoid polluting other tests.
        SetAppLogger(_originalLogger);
    }

    /// <summary>
    /// Reflectively sets <see cref="App.Logger"/> (which has a <c>private set</c>
    /// accessor, only called from <c>InitializeServices</c> / <c>InitializeDemoServices</c>).
    /// Uses the compiler-generated backing field so tests can inject a stub logger.
    /// </summary>
    private static void SetAppLogger(IAppLogger logger)
    {
        var field = typeof(App).GetField("<Logger>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(null, logger);
    }

    /// <summary>
    /// Executes a faulting command, swallowing the expected re-throw from
    /// <c>AsyncRelayCommand.ExecuteAsync</c>. The <c>WatchCommandErrors</c>
    /// handler fires synchronously on the <c>PropertyChanged</c> event before
    /// <c>ExecuteAsync</c> re-throws, so side effects (LastErrorMessage, logger)
    /// are already set when this method returns.
    /// </summary>
    private static async Task ExecuteExpectingFault(IAsyncRelayCommand command)
    {
        try
        {
            await command.ExecuteAsync(null);
        }
        catch (InvalidOperationException)
        {
            // Expected — AsyncRelayCommand re-throws after storing the faulted task.
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Faulting command
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FaultingCommand_SetsLastErrorMessage()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        var vm = new TestViewModel(shouldThrow: true);
        await ExecuteExpectingFault(vm.DoWorkCommand);

        Assert.NotNull(vm.LastErrorMessage);
        Assert.Equal("Something broke", vm.LastErrorMessage);
    }

    [Fact]
    public async Task FaultingCommand_LogsViaAppLogger()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        var vm = new TestViewModel(shouldThrow: true);
        await ExecuteExpectingFault(vm.DoWorkCommand);

        Assert.True(logger.ErrorWasLogged);
        Assert.Contains("Something broke", logger.LastLoggedMessage ?? "");
    }

    [Fact]
    public async Task FaultingCommand_UsesContextInLogEntry()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        var vm = new TestViewModel(shouldThrow: true, watchContext: "Save section");
        await ExecuteExpectingFault(vm.DoWorkCommand);

        Assert.Equal("Save section", logger.LastLoggedContext);
    }

    [Fact]
    public async Task FaultingCommand_DefaultContext_IsCommandFailed()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        var vm = new TestViewModel(shouldThrow: true, watchContext: null);
        await ExecuteExpectingFault(vm.DoWorkCommand);

        Assert.Equal("Command failed", logger.LastLoggedContext);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Successful command
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SuccessfulCommand_LastErrorMessageRemainsNull()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        var vm = new TestViewModel(shouldThrow: false);
        await vm.DoWorkCommand.ExecuteAsync(null);

        Assert.Null(vm.LastErrorMessage);
    }

    [Fact]
    public async Task SuccessfulCommand_DoesNotLogError()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        var vm = new TestViewModel(shouldThrow: false);
        await vm.DoWorkCommand.ExecuteAsync(null);

        Assert.False(logger.ErrorWasLogged);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Edge case: inner exception unwrapping
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FaultingCommand_UnwrapsInnerException()
    {
        var logger = new StubLogger();
        SetAppLogger(logger);

        // AsyncRelayCommand wraps the thrown exception in an AggregateException
        // inside ExecutionTask. WatchCommandErrors should unwrap to the inner
        // exception message via ex.InnerException.
        var vm = new TestViewModel(shouldThrow: true);
        await ExecuteExpectingFault(vm.DoWorkCommand);

        // The message should be the original "Something broke", not the
        // AggregateException's "One or more errors occurred" wrapper.
        Assert.DoesNotContain("One or more errors", vm.LastErrorMessage ?? "");
        Assert.Equal("Something broke", vm.LastErrorMessage);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test infrastructure
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal <see cref="ViewModelBase"/> subclass that exposes an
    /// <see cref="AsyncRelayCommand"/> monitored by <c>WatchCommandErrors</c>.
    /// The command either completes successfully or throws, controlled by
    /// the <paramref name="shouldThrow"/> constructor parameter.
    /// </summary>
    private sealed class TestViewModel : ViewModelBase
    {
        public IAsyncRelayCommand DoWorkCommand { get; }

        public TestViewModel(bool shouldThrow, string? watchContext = null)
        {
            // The lambda must be async so that a thrown exception is captured as
            // a faulted Task (returned to the caller) rather than thrown synchronously
            // before AsyncRelayCommand can assign ExecutionTask. Without async, the
            // PropertyChanged event for ExecutionTask never fires for the faulted state
            // and WatchCommandErrors never sees the failure.
            DoWorkCommand = new AsyncRelayCommand(async () =>
            {
                await Task.CompletedTask; // ensure genuine async Task return
                if (shouldThrow)
                    throw new InvalidOperationException("Something broke");
            });

            WatchCommandErrors(DoWorkCommand, watchContext);
        }
    }

    /// <summary>
    /// Minimal <see cref="IAppLogger"/> that records whether <see cref="LogError"/>
    /// was called and captures the exception message and context string for assertion.
    /// </summary>
    private sealed class StubLogger : IAppLogger
    {
        public bool ThrowOnError { get; set; }
        public event EventHandler<string>? ErrorLogged;

        public bool ErrorWasLogged { get; private set; }
        public string? LastLoggedMessage { get; private set; }
        public string? LastLoggedContext { get; private set; }

        public void LogError(Exception? ex, string? context = null)
        {
            ErrorWasLogged = true;
            LastLoggedMessage = ex?.Message;
            LastLoggedContext = context;
            ErrorLogged?.Invoke(this, context ?? ex?.Message ?? "error");
        }

        public void LogWarning(string message, string? context = null) { }
        public void LogInfo(string message, string? context = null) { }
    }
}
