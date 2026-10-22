using System;

namespace TermPoint.Services;

/// <summary>
/// What a fault injected by <see cref="NetworkFaults"/> does to one <see cref="NetworkFileOps"/> operation.
/// </summary>
internal enum NetworkFaultKind
{
    /// <summary>No fault: the operation runs normally.</summary>
    None,

    /// <summary>
    /// The operation is NOT run. The call returns immediately (without waiting
    /// <see cref="NetworkFileOps.TimeoutMs"/>) exactly what a real timeout returns, and logs the same
    /// "timed out" line the real path logs. Simulates a share that never answered.
    /// </summary>
    TimeOut,

    /// <summary>
    /// The operation IS run to completion and its result is discarded; the call then reports a timeout exactly
    /// as <see cref="TimeOut"/> does. Simulates an abandoned operation that still landed on the share after its
    /// deadline (for example the "ghost rename": the save's File.Move completes after SaveAsync gave up on it).
    /// </summary>
    TimeOutAfterRunning,

    /// <summary>
    /// The call throws <see cref="NetworkFaultDecision.Exception"/>, at the point the real operation would
    /// throw (before it does any work). Simulates a share that answers with an error.
    /// </summary>
    Throw
}

/// <summary>
/// What <see cref="NetworkFaults.Decide"/> is asked about: one logical <see cref="NetworkFileOps"/> operation.
/// </summary>
/// <param name="Label">
/// The operation's label, the same string the code already passes to <see cref="NetworkFileOps.RunAsync{T}(System.Func{T}, string)"/>
/// or logs: "File.Exists", "Directory.Exists", "ProbeFile", "File.Move", "File.Delete", "File.ReadAllText",
/// "File.WriteAllText", "CheckIntegrity", "ComputeHash", "CopyFile", or a caller's own label such as
/// "lock acquire" or "File.Stat" (the save's stat of D: its size and last-write time).
/// </param>
/// <param name="Path">
/// The path the operation works on (the source, for a copy or move), or null when the call site did not name one
/// (a generic <c>RunAsync</c> call such as "lock acquire", which the harness must fault through its label).
/// </param>
/// <param name="DestinationPath">The destination of a copy or move; null for every other operation.</param>
internal readonly record struct NetworkFaultRequest(string Label, string? Path, string? DestinationPath);

/// <summary>
/// The injector's answer for one operation.
/// </summary>
/// <param name="Kind">What to do; <see cref="NetworkFaultKind.None"/> lets the operation run normally.</param>
/// <param name="Exception">The exception to throw; required (by the caller's contract) when <paramref name="Kind"/> is <see cref="NetworkFaultKind.Throw"/>.</param>
internal readonly record struct NetworkFaultDecision(NetworkFaultKind Kind, Exception? Exception = null)
{
    /// <summary>The "no fault" answer.</summary>
    public static NetworkFaultDecision None => new(NetworkFaultKind.None);

    /// <summary>The operation is not run and reports a timeout at once.</summary>
    public static NetworkFaultDecision TimeOut => new(NetworkFaultKind.TimeOut);

    /// <summary>The operation runs to completion, then reports a timeout.</summary>
    public static NetworkFaultDecision TimeOutAfterRunning => new(NetworkFaultKind.TimeOutAfterRunning);

    /// <summary>The operation throws <paramref name="exception"/>.</summary>
    /// <param name="exception">The exception the call throws.</param>
    public static NetworkFaultDecision Throwing(Exception exception) => new(NetworkFaultKind.Throw, exception);
}

/// <summary>
/// Test-only network fault injector for <see cref="NetworkFileOps"/>. When <see cref="Decide"/> is null (always,
/// in the app) every operation runs exactly as before; the only cost is one null check per logical operation.
/// A harness sets <see cref="Decide"/> to make operations on the share time out or throw while local files keep
/// working (by answering <see cref="NetworkFaultDecision.None"/> for paths outside the share).
///
/// <para>The hook is consulted <b>once per logical operation</b>: at the start of each named
/// <see cref="NetworkFileOps"/> helper and each generic <c>RunAsync</c> call. A named helper that is built on
/// <c>RunAsync</c> passes its paths into it, so it is not consulted twice.</para>
///
/// <para>Static state: tests that set <see cref="Decide"/> must reset it to null when they finish.</para>
/// </summary>
internal static class NetworkFaults
{
    /// <summary>
    /// Decides the fate of one operation. Null (the default) means no injection. May be called from any
    /// thread, so it must be thread-safe.
    /// </summary>
    internal static Func<NetworkFaultRequest, NetworkFaultDecision>? Decide { get; set; }

    /// <summary>
    /// Asks the hook (if any) what to do with one operation.
    /// </summary>
    /// <param name="label">The operation's label.</param>
    /// <param name="path">The path involved (source for copy/move), or null when unknown.</param>
    /// <param name="destinationPath">The destination for copy/move, or null.</param>
    /// <returns><see cref="NetworkFaultDecision.None"/> when no hook is set or the hook declines.</returns>
    internal static NetworkFaultDecision Consult(string label, string? path, string? destinationPath)
        => Decide is { } decide
            ? decide(new NetworkFaultRequest(label, path, destinationPath))
            : NetworkFaultDecision.None;
}
