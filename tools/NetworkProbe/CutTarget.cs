using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkProbe;

/// <summary>
/// What the cut is aimed at: the file server, its addresses, and the clumsy (WinDivert) filter that matches only
/// SMB traffic to it.
/// </summary>
/// <param name="Server">The server's name, taken from the UNC path.</param>
/// <param name="Addresses">
/// The server's addresses SMB is cut on and checked on (port 445), as resolved (an IPv6 link-local address keeps its zone
/// id so it can be connected to; the filter text drops it).
/// </param>
/// <param name="IgnoredAddresses">Addresses the name resolved to that did not accept a connection on 445 even with the network up.</param>
/// <param name="Filter">The WinDivert filter clumsy is started with.</param>
/// <param name="PortOnly">True when the filter is port 445 only (also cuts SMB to any other server).</param>
internal sealed record CutTarget(
    string Server,
    IReadOnlyList<IPAddress> Addresses,
    IReadOnlyList<IPAddress> IgnoredAddresses,
    string Filter,
    bool PortOnly)
{
    /// <summary>The SMB port.</summary>
    public const int SmbPort = 445;
}

/// <summary>
/// Works out a <see cref="CutTarget"/> from the share path (spec section 5.2).
/// </summary>
internal static class CutTargetResolver
{
    /// <summary>
    /// Resolves the server of a UNC share path and builds the filter.
    /// </summary>
    /// <param name="sharePath">A path of the form <c>\\server\share\folder</c>.</param>
    /// <param name="portOnly">True for the <c>--port-only</c> filter.</param>
    /// <param name="ct">Cancels the connection checks.</param>
    /// <returns>The target.</returns>
    /// <exception cref="ProbeRefusalException">
    /// The path is not a UNC path, the name does not resolve, the share is on this computer, or the server answers on
    /// no address.
    /// </exception>
    public static async Task<CutTarget> ResolveAsync(string sharePath, bool portOnly, CancellationToken ct)
    {
        var server = ServerOf(sharePath);

        IPAddress[] resolved;
        try { resolved = await Dns.GetHostAddressesAsync(server, ct).ConfigureAwait(false); }
        catch (SocketException ex)
        {
            throw new ProbeRefusalException($"The server name '{server}' does not resolve ({ex.Message}).");
        }

        // Keep each address exactly as resolved, zone id included: a link-local IPv6 address (fe80::...) can only be
        // connected to WITH its zone. Only the filter text drops the zone (BuildFilter), because WinDivert's filter
        // language does not accept one.
        var addresses = resolved
            .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Distinct()
            .ToList();
        if (addresses.Count == 0)
            throw new ProbeRefusalException($"The server name '{server}' resolved to no IPv4 or IPv6 address.");

        if (addresses.Any(IsThisComputer))
            throw new ProbeRefusalException(
                $"'{server}' is this computer (or the loopback address). The probe cuts SMB to the server, so the share must be on ANOTHER computer.");

        // Keep only the addresses that really accept a connection now: an address that never answers (an unroutable
        // IPv6 record, say) could never be told apart from a cut, and would make "network up" false forever.
        var live = new List<IPAddress>();
        var ignored = new List<IPAddress>();
        foreach (var address in addresses)
            (await TcpReachability.CanConnectAsync(address, TcpReachability.CheckTimeout, ct).ConfigureAwait(false) ? live : ignored).Add(address);
        if (live.Count == 0)
            throw new ProbeRefusalException(
                $"'{server}' does not accept a connection on port {CutTarget.SmbPort} at any of its addresses ({string.Join(", ", addresses)}). Is the share reachable right now?");

        return new CutTarget(server, live, ignored, BuildFilter(live, portOnly), portOnly);
    }

    /// <summary>
    /// Builds the WinDivert filter (spec section 5.2): SMB (port 445) to or from the server's addresses, or port 445
    /// alone with <paramref name="portOnly"/>.
    /// </summary>
    /// <param name="addresses">The server's addresses; any IPv6 zone id (<c>%12</c>) is left out of the filter text.</param>
    /// <param name="portOnly">True for the port-only filter.</param>
    /// <returns>The filter text.</returns>
    public static string BuildFilter(IReadOnlyList<IPAddress> addresses, bool portOnly)
    {
        const string port = "tcp.DstPort == 445 or tcp.SrcPort == 445";
        if (portOnly) return port;

        var terms = addresses.Select(StripZone).Select(a => a.AddressFamily == AddressFamily.InterNetworkV6
            ? $"ipv6.DstAddr == {a} or ipv6.SrcAddr == {a}"
            : $"ip.DstAddr == {a} or ip.SrcAddr == {a}");
        return $"({port}) and ({string.Join(" or ", terms)})";
    }

    /// <summary>
    /// Extracts the server name from a UNC path.
    /// </summary>
    /// <param name="sharePath">A path of the form <c>\\server\share\folder</c>.</param>
    /// <returns>The server name.</returns>
    /// <exception cref="ProbeRefusalException">The path is not a UNC path (a drive letter hides the server's name).</exception>
    public static string ServerOf(string sharePath)
    {
        if (!sharePath.StartsWith(@"\\", StringComparison.Ordinal) || sharePath.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new ProbeRefusalException(
                $"--share must be a UNC path such as \\\\server\\share\\folder (got '{sharePath}'). A mapped drive letter hides the server's name, which the probe needs.");
        var server = sharePath.TrimStart('\\').Split('\\')[0];
        if (server.Length == 0)
            throw new ProbeRefusalException($"--share '{sharePath}' has no server name.");
        return server;
    }

    /// <summary>Removes an IPv6 zone id (<c>%12</c>), which WinDivert's filter language does not accept.</summary>
    /// <param name="address">An address.</param>
    /// <returns>The same address without a scope.</returns>
    private static IPAddress StripZone(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetworkV6 ? new IPAddress(address.GetAddressBytes()) : address;

    /// <summary>True for a loopback address or an address of one of this computer's network adapters.</summary>
    /// <param name="address">An address.</param>
    /// <returns>True when the address is this computer's own.</returns>
    private static bool IsThisComputer(IPAddress address)
        => IPAddress.IsLoopback(address)
           || NetworkInterface.GetAllNetworkInterfaces()
               .SelectMany(n => n.GetIPProperties().UnicastAddresses)
               .Any(u => StripZone(u.Address).Equals(address));
}
