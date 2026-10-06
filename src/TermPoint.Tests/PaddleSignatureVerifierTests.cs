using System.Security.Cryptography;
using System.Text;
using TermPoint.LicenseFulfillment;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Verifies <see cref="PaddleSignatureVerifier"/> against Paddle's documented scheme:
/// HMAC-SHA256 of "{ts}:{rawBody}" keyed with the endpoint secret, hex encoded as h1.
/// </summary>
public sealed class PaddleSignatureVerifierTests
{
    private const string Secret = "pdl_ntfset_test_secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = PaddleSignatureVerifier.DefaultTolerance;
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"event_type\":\"transaction.completed\"}");

    private static string Sign(long ts, byte[] body, string secret = Secret)
    {
        var payload = Encoding.UTF8.GetBytes($"{ts}:").Concat(body).ToArray();
        var h1 = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return Convert.ToHexString(h1).ToLowerInvariant();
    }

    private static string Header(long ts, string h1) => $"ts={ts};h1={h1}";

    [Fact]
    public void ValidSignature_Passes()
    {
        var ts = Now.ToUnixTimeSeconds();
        Assert.True(PaddleSignatureVerifier.Verify(Header(ts, Sign(ts, Body)), Body, Secret, Now, Tolerance));
    }

    [Fact]
    public void TamperedBody_Fails()
    {
        var ts = Now.ToUnixTimeSeconds();
        var tampered = Encoding.UTF8.GetBytes("{\"event_type\":\"transaction.completed\",\"x\":1}");
        Assert.False(PaddleSignatureVerifier.Verify(Header(ts, Sign(ts, Body)), tampered, Secret, Now, Tolerance));
    }

    [Fact]
    public void WrongSecret_Fails()
    {
        var ts = Now.ToUnixTimeSeconds();
        Assert.False(PaddleSignatureVerifier.Verify(Header(ts, Sign(ts, Body, "other")), Body, Secret, Now, Tolerance));
    }

    [Fact]
    public void StaleTimestamp_Fails()
    {
        var ts = Now.AddMinutes(-10).ToUnixTimeSeconds();
        Assert.False(PaddleSignatureVerifier.Verify(Header(ts, Sign(ts, Body)), Body, Secret, Now, Tolerance));
    }

    [Fact]
    public void FutureTimestamp_Fails()
    {
        var ts = Now.AddMinutes(10).ToUnixTimeSeconds();
        Assert.False(PaddleSignatureVerifier.Verify(Header(ts, Sign(ts, Body)), Body, Secret, Now, Tolerance));
    }

    [Fact]
    public void ChangedTimestampWithOldSignature_Fails()
    {
        var ts = Now.ToUnixTimeSeconds();
        var h1 = Sign(ts - 1, Body);
        Assert.False(PaddleSignatureVerifier.Verify(Header(ts, h1), Body, Secret, Now, Tolerance));
    }

    [Fact]
    public void RotatedSecret_AnyMatchingH1_Passes()
    {
        var ts = Now.ToUnixTimeSeconds();
        var header = $"ts={ts};h1={Sign(ts, Body, "old")};h1={Sign(ts, Body)}";
        Assert.True(PaddleSignatureVerifier.Verify(header, Body, Secret, Now, Tolerance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("ts=abc;h1=00")]
    [InlineData("ts=99999999999999999999;h1=00")]
    [InlineData("h1=00")]
    [InlineData("ts=1700000000")]
    [InlineData("ts=1700000000;h1=not-hex")]
    public void MalformedHeader_Fails(string? header)
    {
        Assert.False(PaddleSignatureVerifier.Verify(header, Body, Secret, Now, Tolerance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingSecret_Fails(string? secret)
    {
        var ts = Now.ToUnixTimeSeconds();
        Assert.False(PaddleSignatureVerifier.Verify(Header(ts, Sign(ts, Body)), Body, secret, Now, Tolerance));
    }
}
