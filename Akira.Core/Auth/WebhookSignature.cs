using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Akira.Core.Auth;

/// <summary>
/// Signs an outgoing webhook body the way receivers check it: HMAC-SHA256 over "timestamp.body" with the webhook's secret.
/// </summary>
public static class WebhookSignature
{
    public const string Header = "X-Akira-Signature";

    // The timestamp is part of what is signed, so a receiver can refuse an old delivery played again
    public static string Create(string secret, DateTimeOffset timestamp, ReadOnlySpan<byte> body)
    {
        var unix = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signed = new byte[Encoding.UTF8.GetByteCount(unix) + 1 + body.Length];
        var written = Encoding.UTF8.GetBytes(unix, signed);
        signed[written] = (byte)'.';
        body.CopyTo(signed.AsSpan(written + 1));
        var key = Encoding.UTF8.GetBytes(secret);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, signed));
        return $"t={unix},v1={signature}";
    }
}
