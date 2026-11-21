using System.Security.Cryptography;

namespace Akira.Core.Auth;

/// <summary>
/// Creates the short codes a device shows while it waits to be approved.
/// </summary>
public static class PairingCodes
{
    // No 0/O, 1/I/L, 5/S or 8/B: the code is read off a TV across the room and typed on a phone
    public const string Alphabet = "234679ACDEFGHJKMNPQRTUVWXYZ";
    public const int Length = 6;

    /// <summary>
    /// Returns a code like "K7Q-4MX".
    /// </summary>
    public static string New()
    {
        var characters = new char[Length];
        for (var index = 0; index < Length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"{new string(characters, 0, 3)}-{new string(characters, 3, 3)}";
    }

    /// <summary>
    /// Normalizes what a user typed, so "k7q 4mx" and "K7Q-4MX" are the same code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (input is null)
        {
            return null;
        }

        var characters = input.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        if (characters.Length != Length || !characters.All(Alphabet.Contains))
        {
            return null;
        }

        return $"{new string(characters, 0, 3)}-{new string(characters, 3, 3)}";
    }
}
