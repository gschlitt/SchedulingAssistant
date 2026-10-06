using System.Security.Cryptography;
using System.Text;

namespace TermPoint.LicenseFulfillment;

/// <summary>
/// Verifies Paddle Billing webhook signatures.
/// The <c>Paddle-Signature</c> header looks like <c>ts=1671552777;h1=&lt;hex&gt;</c>. The signed payload
/// is <c>"{ts}:{rawBody}"</c>, HMAC-SHA256'd with the notification destination's endpoint secret key.
/// More than one <c>h1</c> may be present while a secret is being rotated; any match is accepted.
/// </summary>
public static class PaddleSignatureVerifier
{
    /// <summary>How far the signed timestamp may differ from "now" (guards against replay).</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <param name="signatureHeader">Value of the <c>Paddle-Signature</c> header.</param>
    /// <param name="rawBody">The request body exactly as received. Do not re-serialize it.</param>
    /// <param name="secret">The notification destination's endpoint secret key.</param>
    public static bool Verify(
        string? signatureHeader,
        byte[] rawBody,
        string? secret,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(secret))
            return false;

        string? ts = null;
        var h1Values = new List<string>();
        foreach (var part in signatureHeader.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim();
            var value = kv[1].Trim();
            if (key == "ts") ts = value;
            else if (key == "h1") h1Values.Add(value);
        }

        if (ts is null || h1Values.Count == 0 || !long.TryParse(ts, out var seconds))
            return false;

        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return false; }

        if ((now - signedAt).Duration() > tolerance)
            return false;

        var prefix = Encoding.UTF8.GetBytes(ts + ":");
        var payload = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(payload, 0);
        rawBody.CopyTo(payload, prefix.Length);

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);

        foreach (var h1 in h1Values)
        {
            byte[] candidate;
            try { candidate = Convert.FromHexString(h1); }
            catch (FormatException) { continue; }

            if (CryptographicOperations.FixedTimeEquals(expected, candidate))
                return true;
        }

        return false;
    }
}
