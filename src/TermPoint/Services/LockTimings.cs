using System;
using System.Globalization;
using TermPoint.Models;

namespace TermPoint.Services;

/// <summary>
/// The lock timers of <see cref="WriteLockService"/> as ONE set, divided by ONE whole-number scale factor (spec item 8.7, 1.2.4).
/// The factor exists so a test build can make the lock's waiting (a heartbeat a minute, a lock stale after three minutes, a
/// takeover confirmed after seventy seconds) take a third of the time, and so field tests of stale locks and takeovers need minutes
/// rather than the better part of an hour. The RATIOS between the timers are what keep the lock safe, so they are scaled together
/// and never one by one.
///
/// <para><b>What is scaled</b> (every value is the production value divided by <see cref="Scale"/>): the heartbeat interval, the
/// poll interval, the stale-lock threshold, the stale confirmation, the wake-check interval and the wake threshold.
/// <b>What is never scaled:</b> <see cref="NetworkFileOps.TimeoutMs"/> (the five-second deadline on every share call),
/// <see cref="NetworkFileOps.MoveRetryDelayMs"/>, the lock-acquire retry delay, the waits on the program's own pending release,
/// the restore's lock-release wait, the autosave interval (a user setting), and anything Windows owns.</para>
///
/// <para><b>When the factor is more than 1.</b> Only when BOTH hold: the install channel is <see cref="InstallChannel.Sideload"/>
/// or <see cref="InstallChannel.Development"/> (never the Store or the downloadable installer, so a customer's copy can never run
/// fast), AND the environment variable <see cref="EnvironmentVariableName"/> is the whole number 2 or 3. Anything else (unset, text,
/// 0, 1, a negative, anything above <see cref="MaxScale"/>) is a factor of 1. The maximum is 3 because the five-second deadline
/// does not scale: on a bad network one heartbeat can take about fifteen seconds, and at 3 the stale threshold is 60 s, still four
/// times that; at 6 it would be 30 s, only twice.</para>
///
/// <para><b>Whose factor judges a lock.</b> A lock file records its writer's factor (<see cref="LockFileData.TimingScale"/>; absent
/// means 1). A computer that decides whether SOMEONE ELSE'S lock is stale uses the timings of the factor in that lock file
/// (<see cref="ForHolder"/>), not its own, because the writer's heartbeat runs at the writer's speed: a test build never judges a
/// normal user's lock stale early, and a normal user never judges a (faster) test build's lock stale late or early by mistake. A
/// computer's own heartbeat, polling and wake check use its own factor (<see cref="Current"/>).</para>
/// </summary>
internal sealed class LockTimings
{
    /// <summary>The environment variable that asks for a factor above 1 (honoured only on a Sideload or Development build).</summary>
    public const string EnvironmentVariableName = "TERMPOINT_TEST_TIMINGS";

    /// <summary>The largest factor ever used (see the class remarks for why it is 3).</summary>
    public const int MaxScale = 3;

    /// <summary>Production heartbeat interval, in seconds, at factor 1.</summary>
    public const int HeartbeatIntervalSeconds = WriteLockService.HeartbeatIntervalSeconds;

    /// <summary>Production reader poll interval, in seconds, at factor 1.</summary>
    public const int PollIntervalSeconds = WriteLockService.PollIntervalSeconds;

    /// <summary>Production stale-lock threshold, in seconds, at factor 1.</summary>
    public const int StaleLockThresholdSeconds = WriteLockService.StaleLockThresholdSeconds;

    /// <summary>Production stale confirmation, in seconds, at factor 1 (a heartbeat interval plus ten seconds).</summary>
    public const int StaleConfirmationSeconds = WriteLockService.StaleConfirmationSeconds;

    /// <summary>Production wake-check interval, in seconds, at factor 1.</summary>
    public const int WakeCheckIntervalSeconds = 30;

    /// <summary>Production wake threshold, in seconds, at factor 1: a gap between wake checks longer than this (three intervals) means the machine was asleep.</summary>
    public const int WakeThresholdSeconds = 90;

    /// <summary>The timings of factor 1: exactly the production values.</summary>
    public static LockTimings Normal { get; } = new(1);

    /// <summary>One set per allowed factor, so <see cref="ForScale"/> never allocates.</summary>
    private static readonly LockTimings[] ByScale = { Normal, Normal, new(2), new(3) };

    /// <summary>This process's timings, resolved once from the install channel and the environment variable.</summary>
    private static readonly Lazy<LockTimings> CurrentTimings = new(ResolveForThisProcess);

    /// <summary>Creates the set for one factor (1 to <see cref="MaxScale"/>).</summary>
    /// <param name="scale">The factor; every timer is its production value divided by it.</param>
    private LockTimings(int scale)
    {
        Scale              = scale;
        HeartbeatInterval  = Divide(HeartbeatIntervalSeconds, scale);
        PollInterval       = Divide(PollIntervalSeconds, scale);
        StaleLockThreshold = Divide(StaleLockThresholdSeconds, scale);
        StaleConfirmation  = Divide(StaleConfirmationSeconds, scale);
        WakeCheckInterval  = Divide(WakeCheckIntervalSeconds, scale);
        WakeThreshold      = Divide(WakeThresholdSeconds, scale);
    }

    /// <summary>The factor: 1 in production, 2 or 3 in a test build that asked for it.</summary>
    public int Scale { get; }

    /// <summary>How often a writer renews its heartbeat (60 s at factor 1).</summary>
    public TimeSpan HeartbeatInterval { get; }

    /// <summary>How often a read-only session polls the lock file (60 s at factor 1).</summary>
    public TimeSpan PollInterval { get; }

    /// <summary>How long a heartbeat must stand still before the lock is stale (180 s at factor 1).</summary>
    public TimeSpan StaleLockThreshold { get; }

    /// <summary>How long a heartbeat must have stood still before a confirmed takeover may go ahead (70 s at factor 1).</summary>
    public TimeSpan StaleConfirmation { get; }

    /// <summary>How often the wake check runs (30 s at factor 1).</summary>
    public TimeSpan WakeCheckInterval { get; }

    /// <summary>The gap between wake checks that means the machine slept (90 s at factor 1).</summary>
    public TimeSpan WakeThreshold { get; }

    /// <summary>True when the factor is above 1 (a test build running fast).</summary>
    public bool IsScaled => Scale > 1;

    /// <summary>
    /// The text appended to the main window's title when <see cref="IsScaled"/>, so a fast build can never be mistaken for a normal one;
    /// empty at factor 1.
    /// </summary>
    public string TitleSuffix => IsScaled ? $" (Test timings ×{Scale.ToString(CultureInfo.InvariantCulture)})" : string.Empty;

    /// <summary>This process's timings: from the install channel and <see cref="EnvironmentVariableName"/>, resolved once (see <see cref="Resolve"/>).</summary>
    public static LockTimings Current => CurrentTimings.Value;

    /// <summary>
    /// The timings for a factor. Any value outside 1 to <see cref="MaxScale"/> (a corrupt or hostile lock file) is treated as 1, the
    /// production timings, which is the patient choice when judging someone else's lock.
    /// </summary>
    /// <param name="scale">The factor.</param>
    /// <returns>The shared set for that factor.</returns>
    public static LockTimings ForScale(int scale) => scale is >= 1 and <= MaxScale ? ByScale[scale] : Normal;

    /// <summary>
    /// The timings that judge a lock held by <paramref name="holder"/>: the factor the holder recorded in its lock file
    /// (<see cref="LockFileData.TimingScale"/>), or 1 when the holder is unknown or wrote none (every 1.2.3 lock).
    /// </summary>
    /// <param name="holder">What the lock file holds; may be null.</param>
    /// <returns>The holder's timings.</returns>
    public static LockTimings ForHolder(LockFileData? holder) => ForScale(holder?.TimingScale ?? 1);

    /// <summary>
    /// Decides the factor from the install channel and the environment variable's text. Pure: no environment or channel access, so
    /// every rule is unit-tested.
    /// </summary>
    /// <param name="channel">How this copy was installed.</param>
    /// <param name="value">The variable's raw text; null or empty when it is not set.</param>
    /// <param name="note">A line for the log when a value was set but ignored; null otherwise.</param>
    /// <returns>1, 2 or 3. Above 1 only on <see cref="InstallChannel.Sideload"/> or <see cref="InstallChannel.Development"/> with the value 2 or 3.</returns>
    public static int Resolve(InstallChannel channel, string? value, out string? note)
    {
        note = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return 1;

        if (channel is InstallChannel.Store or InstallChannel.DirectDownload)
        {
            note = $"[WriteLockService] {EnvironmentVariableName} ignored: test timings are not available on a {channel} install.";
            return 1;
        }

        // Digits only (no sign, no decimal point, no separators); anything else, and anything outside 2..MaxScale, means 1.
        if (text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var scale)
            && scale is >= 2 and <= MaxScale)
            return scale;

        note = $"[WriteLockService] {EnvironmentVariableName}=\"{text}\" ignored: expected 2 or 3 (maximum {MaxScale}); using normal timings.";
        return 1;
    }

    /// <summary>Resolves this process's timings from the real channel and environment, and logs what was ignored or applied.</summary>
    /// <returns>This process's timings.</returns>
    private static LockTimings ResolveForThisProcess()
    {
        var scale = Resolve(PlatformCapabilities.InstallChannel, Environment.GetEnvironmentVariable(EnvironmentVariableName), out var note);
        var line = note ?? (scale > 1
            ? $"[WriteLockService] Test timings x{scale}: heartbeat every {Divide(HeartbeatIntervalSeconds, scale).TotalSeconds:0.#} s, a lock is stale after {Divide(StaleLockThresholdSeconds, scale).TotalSeconds:0.#} s."
            : null);
        if (line is not null)
        {
            try { App.Logger.LogInfo(line); }
            catch { /* the logger may not exist yet; the timings do not depend on it */ }
        }
        return ForScale(scale);
    }

    /// <summary>A production number of seconds divided by a factor, as a time span.</summary>
    /// <param name="seconds">The production seconds.</param>
    /// <param name="scale">The factor.</param>
    /// <returns>The scaled duration.</returns>
    private static TimeSpan Divide(int seconds, int scale) => TimeSpan.FromSeconds((double)seconds / scale);
}
