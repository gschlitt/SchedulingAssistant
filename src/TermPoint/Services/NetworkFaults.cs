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
    Throw,

    /// <summary>
    /// The operation IS run, but inside the deadline-guarded part it first blocks on a gate the injector controls
    /// (<see cref="NetworkFaultDecision.EnterHang"/>), so the ordinary deadline race decides: the caller sees the same timeout (and
    /// the same log line and abandoned-task observer) a real hung share produces, and when the gate opens the operation
    /// really runs and lands late. Simulates a share call that blocks instead of failing; unlike <see cref="TimeOut"/>, the
    /// code under test has to survive the block itself.
    /// </summary>
    Hang
}

/// <summary>
/// Where a <see cref="NetworkFaultKind.Hang"/> blocks inside a bulk copy (the other operations have one guarded part only).
/// </summary>
internal enum NetworkHangPoint
{
    /// <summary>The first deadline-guarded step: the open for a copy or a hash; the whole operation for the others.</summary>
    Start,

    /// <summary>
    /// A copy's chunk write, immediately before the real write call: the spot field bug A1 blocked at (1.2.3: the write
    /// call itself blocked synchronously before its deadline race had begun). Ignored by every operation but a copy.
    /// </summary>
    ChunkWrite
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
/// <param name="EnterHang">
/// For <see cref="NetworkFaultKind.Hang"/>: called on the pool thread, inside the deadline-guarded task, where the real call would
/// block. It blocks until the injector lets the operation go and returns a scope that is disposed when the operation has run
/// (so the injector can tell the late landing is over). Null means "nothing to block on".
/// </param>
/// <param name="HangPoint">For <see cref="NetworkFaultKind.Hang"/>: where a bulk copy blocks (see <see cref="NetworkHangPoint"/>).</param>
internal readonly record struct NetworkFaultDecision(
    NetworkFaultKind Kind, Exception? Exception = null,
    Func<IDisposable>? EnterHang = null, NetworkHangPoint HangPoint = NetworkHangPoint.Start)
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

    /// <summary>The operation runs, blocked behind a gate inside its deadline-guarded part; see <see cref="NetworkFaultKind.Hang"/>.</summary>
    /// <param name="enterHang">Blocks until the operation may go on; the returned scope is disposed when it has run.</param>
    /// <param name="point">Where a bulk copy blocks; ignored by the other operations.</param>
    public static NetworkFaultDecision Hanging(Func<IDisposable> enterHang, NetworkHangPoint point = NetworkHangPoint.Start)
        => new(NetworkFaultKind.Hang, null, enterHang, point);
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
