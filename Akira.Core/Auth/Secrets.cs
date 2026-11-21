using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Akira.Core.Auth;

/// <summary>
/// Creates and hashes the secrets the server hands out.
/// </summary>
public static class Secrets
{
    public const string DeviceTokenPrefix = "pdt_";
    public const string ApiKeyPrefix = "pak_";
    public const string LinkTokenPrefix = "pdl_";
    public const string WebhookSecretPrefix = "whsec_";
    public const int DisplayPrefixLength = 12;

    // The prefixes make a leaked secret recognizable in a log or a repository, and tell the
    // server which table to look in. 32 random bytes need no slow hash: there is nothing to guess.
    public static string NewDeviceToken() => DeviceTokenPrefix + Random(32);

    public static string NewApiKey() => ApiKeyPrefix + Random(32);

    public static string NewLinkToken() => LinkTokenPrefix + Random(32);

    public static string NewPollSecret() => Random(32);

    public static string NewWebhookSecret() => WebhookSecretPrefix + Random(32);

    public static string Hash(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string Random(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}
