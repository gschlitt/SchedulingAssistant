#if DEBUG
using System.Globalization;

namespace TermPoint.Services;

/// <summary>
/// A DEBUG-ONLY switch for hand-testing the exit guard (spec item 15): the environment variable
/// <c>TERMPOINT_DEBUG_SLOW_EXIT_SAVE=&lt;seconds&gt;</c> makes the window-close teardown wait that many seconds
/// JUST BEFORE the exit save, so a developer can click X again, try to type, and wait out the guard's two-minute
/// backstop without needing a slow network share.
///
/// <para><b>Value.</b> A whole number of seconds from 0 to <see cref="MaxSeconds"/>. Anything else (a sign, a
/// decimal point, text, a number out of range) is ignored and one log line says so, so a typo can never break a
/// build that is being used for real work. 0 is valid and means "no wait".</para>
///
/// <para><b>DEBUG builds only.</b> This whole file is compiled out of Release (the same pattern as
/// <see cref="CheckoutFaultPlan"/>), and so is its one call site in
/// <see cref="SessionLifecycle.TeardownForCloseAsync"/>: in a Release build neither the variable nor this code
/// exists, so a stray variable on a customer machine cannot change how a shipped build behaves. The wait is a
/// pure delay in the lifecycle; it lives nowhere near <see cref="CheckoutService"/> or any save step.</para>
///
/// <para>The variable is read ONCE, the first time the teardown asks. <see cref="Parse"/> and the wait itself are
/// pure/seamed, so they are unit-tested.</para>
/// </summary>
internal static class SlowExitSave
{
    /// <summary>The environment variable read by <see cref="WaitIfRequestedAsync"/> (DEBUG builds only).</summary>
    public const string EnvironmentVariableName = "TERMPOINT_DEBUG_SLOW_EXIT_SAVE";

    /// <summary>The largest accepted wait, in seconds (ten minutes: past the guard's two-minute backstop with room to spare).</summary>
    public const int MaxSeconds = 600;

    /// <summary>Where the raw value comes from; the real environment unless a test swaps it (<see cref="SetSourceForTests"/>).</summary>
    private static Func<string?> _source = ReadEnvironment;

    /// <summary>The clock the wait runs on; the system clock unless a test swaps it.</summary>
    private static TimeProvider _time = TimeProvider.System;

    /// <summary>True once the raw value has been read and parsed (the "read once" rule).</summary>
    private static bool _read;

    /// <summary>The parsed wait, or null when the variable is unset or was ignored.</summary>
    private static TimeSpan? _delay;

    /// <summary>The value that was rejected, to be logged once; null when nothing was rejected or it has been logged.</summary>
    private static string? _rejected;

    /// <summary>Guards the one-time read; the teardown runs on the UI thread but the guard costs nothing.</summary>
    private static readonly object Gate = new();

    /// <summary>The real environment read.</summary>
    /// <returns>The variable's value, or null when it is not set.</returns>
    private static string? ReadEnvironment() => Environment.GetEnvironmentVariable(EnvironmentVariableName);

    /// <summary>
    /// Parses the variable's text. Pure: no environment access.
    /// </summary>
    /// <param name="text">The raw text, e.g. <c>20</c>; may be null or empty.</param>
    /// <returns>
    /// The wait, or null when <paramref name="text"/> is empty or not a whole number of seconds from 0 to
    /// <see cref="MaxSeconds"/> (surrounding whitespace is allowed; signs, decimals and separators are not).
    /// </returns>
    public static TimeSpan? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.All(char.IsAsciiDigit))
            return null;

        // int.TryParse also rejects a digit string too long for an int; the range check does the rest.
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > MaxSeconds)
            return null;

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Waits for the configured time, if any, logging a line first. Does nothing when the variable is unset,
    /// 0, or ignored (an ignored value is logged once instead). Called from the teardown immediately before the
    /// exit save.
    /// </summary>
    /// <param name="logger">Where the one log line (the wait, or the ignored value) goes.</param>
    /// <returns>A task that completes when the wait is over (immediately when there is none).</returns>
    public static async Task WaitIfRequestedAsync(IAppLogger logger)
    {
        TimeSpan? delay;
        string? rejected;
        lock (Gate)
        {
            if (!_read)
            {
                var raw = _source();
                _delay    = Parse(raw);
                _rejected = _delay is null && !string.IsNullOrWhiteSpace(raw) ? raw : null;
                _read     = true;
            }
            delay     = _delay;
            rejected  = _rejected;
            _rejected = null; // log a rejected value once, not on every later exit attempt
        }

        if (rejected is not null)
            logger.LogInfo($"[Debug] {EnvironmentVariableName}=\"{rejected}\" ignored: expected a whole number of seconds from 0 to {MaxSeconds}.");

        if (delay is not { } wait || wait <= TimeSpan.Zero)
            return;

        logger.LogInfo($"[Debug] {EnvironmentVariableName} is set: waiting {wait.TotalSeconds:0} s before the exit save.");
        await Task.Delay(wait, _time);
    }

    /// <summary>
    /// Test seam: replaces where the raw value comes from and the clock the wait runs on, and forgets any value
    /// already read. Call with no arguments to go back to the real environment and clock.
    /// </summary>
    /// <param name="source">Returns the raw value; null restores the real environment read.</param>
    /// <param name="time">The clock for the wait; null restores the system clock.</param>
    internal static void SetSourceForTests(Func<string?>? source = null, TimeProvider? time = null)
    {
        lock (Gate)
        {
            _source   = source ?? ReadEnvironment;
            _time     = time ?? TimeProvider.System;
            _read     = false;
            _delay    = null;
            _rejected = null;
        }
    }
}
#endif
