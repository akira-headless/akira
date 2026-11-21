using System.Text.Json;

namespace Akira.Infrastructure.Tests;

/// <summary>
/// Reads real Radarr 6 and Sonarr 4 responses saved under Fixtures/.
/// </summary>
public static class Fixtures
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static T Load<T>(string name) => JsonSerializer.Deserialize<T>(Read(name), SerializerOptions)!;
}
