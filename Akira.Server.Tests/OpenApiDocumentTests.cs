using System.Net;
using System.Text.Json;
using Akira.Server.Models.Health;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests;

public class OpenApiDocumentTests
{
    [Fact]
    public async Task Document_DescribesVersionedApi()
    {
        using var document = await GetDocumentAsync();

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();

        Assert.Contains("/api/v1/health", paths);
        Assert.All(paths, path => Assert.StartsWith("/api/v1/", path));

        var operationIds = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => operation.Value.ValueKind == JsonValueKind.Object && operation.Value.TryGetProperty("operationId", out _))
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .ToList();
        Assert.Equal(operationIds.Count, operationIds.Distinct().Count());
    }

    // The document is what clients generate their types from, so its enum values have to be
    // exactly what the serializer writes. Both come from the same JSON options today, this
    // keeps it that way.
    [Fact]
    public async Task Document_EnumValuesMatchSerializer()
    {
        using var document = await GetDocumentAsync();

        var values = document.RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty(nameof(HealthState))
            .GetProperty("enum").EnumerateArray().Select(value => value.GetString())
            .ToList();

        Assert.Equal(Enum.GetNames<HealthState>().Select(JsonNamingPolicy.CamelCase.ConvertName), values);
    }

    [Fact]
    public async Task Document_SharedEnumsAreNotNullable()
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var nullable = schemas.EnumerateObject()
            .Where(schema => schema.Value.TryGetProperty("enum", out var values)
                && values.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.Null))
            .Select(schema => schema.Name);

        Assert.Empty(nullable);

        var auditScope = schemas.GetProperty("AuditEntryModel").GetProperty("properties").GetProperty("scope");
        Assert.Contains(auditScope.GetProperty("oneOf").EnumerateArray(),
            option => option.TryGetProperty("type", out var type) && type.GetString() == "null");
    }

    [Theory]
    [InlineData("/api/v1/items", "type", new[] { "movie", "series", "season", "episode" })]
    [InlineData("/api/v1/items", "sort", new[] { "title", "added", "year" })]
    [InlineData("/api/v1/items/{id}/images/{kind}", "kind", new[] { "poster", "fanart", "banner", "logo", "still" })]
    public async Task Document_ListsAllowedValuesOfEnumParameters(string path, string name, string[] expected)
    {
        using var document = await GetDocumentAsync();

        var parameter = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name);

        Assert.Equal(expected, parameter.GetProperty("schema").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Document_DescribesEveryEventPayload()
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var name in new[] { "LibraryItemPayload", "FileReplacedPayload", "WatchStatePayload", "TrackPreferencePayload",
                     "ScanProgressPayload", "StreamPayload", "ActionCompletedPayload" })
        {
            Assert.True(schemas.TryGetProperty(name, out var schema), $"{name} is missing");
            Assert.True(schema.GetProperty("properties").EnumerateObject().Any());
        }

        var scan = schemas.GetProperty("ScanProgressPayload").GetProperty("properties");
        Assert.True(scan.TryGetProperty("instances", out _));
    }

    private static async Task<JsonDocument> GetDocumentAsync()
    {
        await using var factory = new AkiraApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
