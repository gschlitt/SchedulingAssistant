using System.Net;
using System.Net.Sockets;

namespace NetworkProbe;

/// <summary>
/// Fresh TCP connection attempts to the file server's SMB port (445). The probe uses them as its independent witness
/// of whether the network is cut: a brand-new connect cannot be answered from any cache, unlike a file call, and it
/// does not depend on the code under test.
/// </summary>
internal static class TcpReachability
{
    /// <summary>How long a connection attempt may take before it counts as "no answer" (spec section 5.3: 2 s).</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tries one fresh TCP connection.
    /// </summary>
    /// <param name="address">The server address.</param>
    /// <param name="timeout">How long to wait for the connection.</param>
    /// <param name="ct">Cancels the attempt (and then throws).</param>
    /// <returns>True when the connection was made within the timeout; false when it failed or timed out.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task<bool> CanConnectAsync(IPAddress address, TimeSpan timeout, CancellationToken ct)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(address, CutTarget.SmbPort, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // our own timeout fired: no answer
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            return false; // refused, unreachable, reset: no connection
        }
    }

    /// <summary>
    /// Tries every address at once.
    /// </summary>
    /// <param name="addresses">The server's addresses.</param>
    /// <param name="timeout">How long each attempt may take.</param>
    /// <param name="ct">Cancels the attempts.</param>
    /// <returns>True when a connection was made to EVERY address (the network is up).</returns>
    public static async Task<bool> AllConnectAsync(IReadOnlyList<IPAddress> addresses, TimeSpan timeout, CancellationToken ct)
    {
        var results = await Task.WhenAll(addresses.Select(a => CanConnectAsync(a, timeout, ct))).ConfigureAwait(false);
        return results.All(r => r);
    }

    /// <summary>
    /// Tries every address at once.
    /// </summary>
    /// <param name="addresses">The server's addresses.</param>
    /// <param name="timeout">How long each attempt may take.</param>
    /// <param name="ct">Cancels the attempts.</param>
    /// <returns>True when NO connection could be made to ANY address (the cut is in effect on every address).</returns>
    public static async Task<bool> NoneConnectAsync(IReadOnlyList<IPAddress> addresses, TimeSpan timeout, CancellationToken ct)
    {
        var results = await Task.WhenAll(addresses.Select(a => CanConnectAsync(a, timeout, ct))).ConfigureAwait(false);
        return results.All(r => !r);
    }
}
