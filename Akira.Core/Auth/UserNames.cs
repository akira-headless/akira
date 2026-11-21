namespace Akira.Core.Auth;

public static class UserNames
{
    public const int MaxLength = 100;

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxLength && !name.Any(char.IsControl);
}
