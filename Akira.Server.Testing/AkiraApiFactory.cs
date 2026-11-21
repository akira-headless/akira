using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Akira.Server.Authentication;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Server.Workers;

namespace Akira.Server.Testing;

public sealed class AkiraApiFactory : WebApplicationFactory<Program>
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"akira_api_{Guid.NewGuid():N}.db");
    private readonly string _imageCachePath = Path.Combine(Path.GetTempPath(), $"akira_images_{Guid.NewGuid():N}");
    private readonly DatabaseOptions? _database;

    public AkiraApiFactory(DatabaseOptions? database = null)
    {
        _database = database;
    }

    public Dictionary<string, string> Settings { get; init; } = [];

    public List<ArrInstanceOptions> Instances { get; init; } = [];

    // Off for the tests about setup itself, which need a server without users
    public bool SeedAdmin { get; init; } = true;

    public TimeProvider? Time { get; init; }

    // Replaces services for one test, such as fake Radarr and Sonarr clients
    public Action<IServiceCollection>? TestServices { get; init; }

    /// <summary>
    /// Creates a client without credentials. CreateClient sends the test admin's API key.
    /// </summary>
    public HttpClient CreateAnonymousClient() => Server.CreateClient();

    public HttpClient CreateClient(string bearerToken)
    {
        var client = Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        if (SeedAdmin)
        {
            client.DefaultRequestHeaders.Add(AkiraAuthenticationHandler.ApiKeyHeader, TestAdmin.ApiKey);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var database = _database ?? new DatabaseOptions { Provider = DatabaseProvider.Sqlite, SqlitePath = _sqlitePath };

        Use(builder, nameof(DatabaseOptions.Provider), database.Provider.ToString());
        Use(builder, nameof(DatabaseOptions.SqlitePath), database.SqlitePath);
        Use(builder, nameof(DatabaseOptions.Host), database.Host);
        Use(builder, nameof(DatabaseOptions.Port), database.Port.ToString());
        Use(builder, nameof(DatabaseOptions.Name), database.Name);
        Use(builder, nameof(DatabaseOptions.Username), database.Username);
        Use(builder, nameof(DatabaseOptions.Password), database.Password);
        builder.UseSetting($"{ImageCacheOptions.SectionName}:{nameof(ImageCacheOptions.Path)}", _imageCachePath);

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, LoopbackClient>());
        builder.ConfigureTestServices(services =>
        {
            // Replaces whatever instances configuration or the environment lists, so a test never syncs
            // a real Radarr or Sonarr. Validation still runs on the result.
            services.PostConfigure<ArrOptions>(options => options.Instances = Instances.ToList());

            // Sync, probing, webhook refreshes and event cleanup are tested directly. In the background they would race the tests that seed the library.
            var workers = new[] { typeof(LibrarySyncWorker), typeof(ProbeWorker), typeof(LibraryRefreshWorker), typeof(EventRetentionWorker),
                typeof(SubtitleRequestWorker), typeof(TrickplayWorker), typeof(TranscodeCleanupWorker), typeof(OutgoingWebhookWorker), typeof(TraktWorker), typeof(MarkerWorker), typeof(ImageHashWorker) };
            foreach (var worker in services.Where(service => workers.Contains(service.ImplementationType)).ToList())
            {
                services.Remove(worker);
            }

            if (SeedAdmin)
            {
                services.AddHostedService<TestAdmin>();
            }

            if (Time is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(Time));
            }

            TestServices?.Invoke(services);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_sqlitePath + suffix);
        }

        if (Directory.Exists(_imageCachePath))
        {
            Directory.Delete(_imageCachePath, recursive: true);
        }
    }

    private static void Use(IWebHostBuilder builder, string setting, string value) =>
        builder.UseSetting($"{DatabaseOptions.SectionName}:{setting}", value);

    private sealed class LoopbackClient : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(call => context =>
            {
                context.Connection.RemoteIpAddress ??= System.Net.IPAddress.Loopback;
                return call(context);
            });
            next(app);
        };
    }
}
