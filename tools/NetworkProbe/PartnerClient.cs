using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace NetworkProbe;

/// <summary>
/// The runner's end of the control channel to the partner (<c>--share-semantics --role runner</c>): sends one command line,
/// reads one reply line, and times the exchange on the runner's own stopwatch. Commands are strictly one at a time.
/// </summary>
internal sealed class PartnerClient : IDisposable
{
    /// <summary>How long any one command may take before the partner is considered gone (HASH can legitimately wait a while).</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The connection.</summary>
    private readonly TcpClient _tcp;

    /// <summary>Reads reply lines.</summary>
    private readonly StreamReader _reader;

    /// <summary>Writes command lines.</summary>
    private readonly StreamWriter _writer;

    /// <summary>Wraps a connected socket.</summary>
    /// <param name="tcp">The connected client.</param>
    private PartnerClient(TcpClient tcp)
    {
        _tcp = tcp;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var stream = tcp.GetStream();
        _reader = new StreamReader(stream, utf8);
        _writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>
    /// Connects to the partner.
    /// </summary>
    /// <param name="host">The partner's host name or address.</param>
    /// <param name="port">The partner's TCP port.</param>
    /// <returns>The connected client.</returns>
    /// <exception cref="ProbeRefusalException">The partner did not answer within 10 s (not running, wrong address, or a firewall).</exception>
    public static async Task<PartnerClient> ConnectAsync(string host, int port)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return new PartnerClient(tcp);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            tcp.Dispose();
            throw new ProbeRefusalException(
                $"Could not connect to the partner at {host}:{port} ({ex.GetType().Name}). Is the partner running there, " +
                "and does its computer allow inbound TCP on that port?");
        }
    }

    /// <summary>
    /// Sends a command and waits for its reply.
    /// </summary>
    /// <param name="verb">The command verb (see <see cref="ShareProtocol.Verbs"/>).</param>
    /// <param name="args">The command's arguments (they must not contain tabs or line breaks).</param>
    /// <returns>The parsed reply, with <see cref="PartnerReply.RoundTripMs"/> set from the runner's stopwatch. An ERR reply is returned, not thrown.</returns>
    /// <exception cref="ProbeAbortException">The connection broke, or the partner did not answer in time.</exception>
    public async Task<PartnerReply> SendAsync(string verb, params string[] args)
    {
        var line = string.Join(ShareProtocol.Sep, new[] { verb }.Concat(args));
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(CommandTimeout);
            await _writer.WriteLineAsync(line.AsMemory(), cts.Token).ConfigureAwait(false);
            var reply = await _reader.ReadLineAsync(cts.Token).ConfigureAwait(false)
                        ?? throw new ProbeAbortException($"The partner closed the connection while answering {verb}.");
            var rtt = sw.Elapsed.TotalMilliseconds;
            return ShareProtocol.ParseReply(reply) with { RoundTripMs = rtt };
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or FormatException)
        {
            throw new ProbeAbortException($"The partner stopped answering during {verb} ({ex.GetType().Name}). Restart it and run again.");
        }
    }

    /// <summary>Asks the partner to end the connection (best effort) and closes the socket.</summary>
    public void Dispose()
    {
        try { _writer.WriteLine(ShareProtocol.Verbs.Quit); } catch { /* already gone */ }
        _tcp.Dispose();
    }
}
