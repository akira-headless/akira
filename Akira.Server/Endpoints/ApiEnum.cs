namespace Akira.Server.Endpoints;

/// <summary>
/// Represents an enum bound from a route or query value by name only, case-insensitively.
/// </summary>
public readonly record struct ApiEnum<T>(T Value) where T : struct, Enum
{
    // The default enum binding is case-sensitive, so it refused the camelCase values the API
    // writes itself, and it accepts any number, including ones that are not a defined member
    public static bool TryParse(string? value, out ApiEnum<T> result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)
            || !char.IsLetter(value[0])
            || value.Contains(',')
            || !Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            return false;
        }

        result = new ApiEnum<T>(parsed);
        return true;
    }
}
