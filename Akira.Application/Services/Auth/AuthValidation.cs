namespace Akira.Server.Services.Auth;

internal static class AuthValidation
{
    public const int MaxNameLength = 100;
    public const int MaxClientLength = 50;

    public static string? Text(string? value, string field, int maxLength, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required ? $"{field} is required." : null;
        }

        return value.Trim().Length > maxLength || value.Any(char.IsControl)
            ? $"{field} is at most {maxLength} characters, without control characters."
            : null;
    }

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
