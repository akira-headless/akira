using System.Globalization;
using System.Security.Cryptography;

namespace Akira.Core.Auth;

/// <summary>
/// Validates, hashes and verifies profile PINs.
/// </summary>
public static class Pins
{
    public const int MinLength = 4;
    public const int MaxLength = 8;

    private const string Algorithm = "pbkdf2-sha256";
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    /// <summary>
    /// Hashes a PIN into "pbkdf2-sha256$iterations$salt$hash", so the parameters can change later.
    /// </summary>
    public static string Hash(string pin)
    {
        if (!IsValid(pin))
        {
            throw new ArgumentException($"A PIN is {MinLength} to {MaxLength} digits.", nameof(pin));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Algorithm, Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string? pin, string? storedHash)
    {
        if (pin is null || storedHash is null)
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4
            || parts[0] != Algorithm
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
