using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Akira.Contracts.Accounts;
using Akira.Contracts.Bridges;
using Akira.Contracts.Events;
using Akira.Contracts.Library;
using Akira.Contracts.Media;
using Akira.Contracts.Playback;
using Akira.Contracts.Subtitles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Akira.Server.Authentication;
using Akira.Server.Bridges;
using Akira.Server.Controllers;
using Akira.Core;
using Akira.Core.Enum;
using Akira.Server.Endpoints;
using Akira.Server.Events;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Images;
using Akira.Infrastructure.Integrations.Trakt;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Interfaces.Sync;
using Akira.Infrastructure.Markers;
using Akira.Infrastructure.Probing;
using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Sync;
using Akira.Infrastructure.Transcoding;
using Akira.Infrastructure.Trickplay;
using Akira.Infrastructure.Updates;
using Akira.Server.Interfaces.Services;
using Akira.Migrations;
using Akira.Server.Playback;
using Akira.Server.Plugins;
using Akira.Server.Services;
using Akira.Server.Services.Actions;
using Akira.Server.Services.Auth;
using Akira.Server.Services.Events;
using Akira.Server.Services.Playback;
using Akira.Server.Services.Subtitles;
using Akira.Server.Services.Trakt;
using Akira.Server.Services.Webhooks;
using Akira.Server.Webhooks;
using Akira.Server.Workers;

namespace Akira.Server.Extensions;

public static class ServiceCollectionExtensions
{
    public const string ApiDocumentName = "v1";

    // Events carry these as a free form payload, so no operation would put them in the document
    private static readonly Type[] EventPayloadTypes =
    [
        typeof(Models.Events.LibraryItemPayload), typeof(Models.Events.FileReplacedPayload), typeof(Models.Events.WatchStatePayload),
        typeof(Models.Events.TrackPreferencePayload), typeof(Models.Events.ScanProgressPayload), typeof(Models.Events.StreamPayload),
        typeof(Models.Actions.ActionCompletedPayload), typeof(Models.Subtitles.SubtitleRequestModel), typeof(Models.Events.PlaybackPayload), typeof(Models.Remote.RemoteCommandPayload),
        typeof(Models.Webhooks.OutgoingWebhookDelivery)
    ];

    public static void Configure(this WebApplicationBuilder builder)
    {
        // Registered before the controllers, so problem results are written by the plain writer and not by MVC's,
        // which writes them as ProblemDetails and drops the errors of a validation problem
        builder.Services.AddProblemDetails();

        // Controllers read request bodies with their own options and write results with the HTTP ones, so both get the same
        builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
        var controllers = builder.Services.AddControllers(options =>
            {
                // A missing value reaches the service, which answers with the problem it knows best
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

                // Bodies are application/json and nothing else, as the document says
                foreach (var formatter in options.InputFormatters.OfType<SystemTextJsonInputFormatter>())
                {
                    formatter.SupportedMediaTypes.Clear();
                    formatter.SupportedMediaTypes.Add("application/json");
                }
            })
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));

        builder.ConfigureOpenApi();
        builder.ConfigureDatabase();
        builder.ConfigureIntegrations();
        builder.ConfigureLibrary();
        builder.Services.AddOptions<ProxyOptions>()
            .Bind(builder.Configuration.GetSection(ProxyOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ProxyOptions>, ProxyOptionsValidator>();
        builder.ConfigureProbing();
        builder.ConfigureAuthentication();
        builder.ConfigurePlayback();
        builder.ConfigureEvents();
        builder.ConfigureSubtitles();
        builder.ConfigurePlugins(controllers);
        builder.ConfigureServices();
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        // Without numbers: 99 would bind to a value the enum does not have, the hole ApiEnum closes for routes
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

        // The web defaults accept "24" for a numeric property. Every client is typed against
        // the OpenAPI document, so a string where a number belongs should fail with a 400.
        options.NumberHandling = JsonNumberHandling.Strict;
    }

    private static void ConfigureOpenApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi(ApiDocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Akira API";
                document.Info.Version = ServerVersion.Number;

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["deviceToken"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        Description = "A device token from pairing, or a profile session token on a shared device."
                    },
                    ["apiKey"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = AkiraAuthenticationHandler.ApiKeyHeader,
                        Description = "An API key for scripts and integrations."
                    }
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("deviceToken", document)] = [] },
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("apiKey", document)] = [] }
                ];
                return Task.CompletedTask;
            });

            options.AddDocumentTransformer(async (document, context, cancellationToken) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                foreach (var type in EventPayloadTypes)
                {
                    var schema = await context.GetOrCreateSchemaAsync(type, cancellationToken: cancellationToken);
                    document.Components.Schemas.TryAdd(type.Name, schema);
                }
            });

            // One nullable use puts null into the shared enum schema and so makes every use nullable;
            // a nullable use already wraps the reference in a oneOf with null
            options.AddDocumentTransformer((document, _, _) =>
            {
                foreach (var schema in document.Components?.Schemas?.Values.OfType<OpenApiSchema>() ?? [])
                {
                    if (schema.Enum is { } values && values.Any(value => value is null))
                    {
                        schema.Enum = values.Where(value => value is not null).ToList();
                    }
                }

                return Task.CompletedTask;
            });

            // An action for GET and HEAD has one endpoint name, and an operationId has to be unique
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (HttpMethods.IsHead(context.Description.HttpMethod ?? string.Empty) && operation.OperationId is { } id)
                {
                    operation.OperationId = id + "Head";
                }

                return Task.CompletedTask;
            });

            // ApiEnum binds through TryParse, which the generator can only describe as a plain string
            options.AddOperationTransformer((operation, context, _) =>
            {
                foreach (var description in context.Description.ParameterDescriptions)
                {
                    // MVC describes a TryParse type as a string, only the parameter itself still has the declared type
                    var declared = description.ParameterDescriptor?.ParameterType ?? description.Type;
                    var type = Nullable.GetUnderlyingType(declared) ?? declared;
                    if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ApiEnum<>))
                    {
                        continue;
                    }

                    if (operation.Parameters?.FirstOrDefault(candidate => candidate.Name == description.Name) is not OpenApiParameter parameter)
                    {
                        continue;
                    }

                    var enumType = type.GetGenericArguments()[0];
                    var values = Enum.GetNames(enumType)
                        .Select(name => (JsonNode)JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name)))
                        .ToList();
                    parameter.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = values
                    };
                }

                return Task.CompletedTask;
            });
        });
    }

    private static void ConfigureDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<DatabaseOptions>()
            .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        var contentRootPath = builder.Environment.ContentRootPath;
        builder.Services.AddSingleton(provider => DatabaseConnection.Create(
            provider.GetRequiredService<IOptions<DatabaseOptions>>().Value,
            contentRootPath));

        builder.Services.AddDbContext<AkiraDbContext>((provider, options) =>
            DatabaseConfiguration.ConfigureDbContext(options, provider.GetRequiredService<DatabaseConnection>()));

        builder.Services.AddFluentMigrator(provider => provider.GetRequiredService<DatabaseConnection>());
    }

    private static void ConfigureIntegrations(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ArrOptions>()
            .Bind(builder.Configuration.GetSection(ArrOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ArrOptions>, ArrOptionsValidator>();

        builder.Services.AddHttpClient(ArrHttpClientConfiguration.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(UpstreamHttp.CreateHandler);
        builder.Services.AddSingleton<IArrClientFactory, ArrClientFactory>();

        builder.Services.AddHttpClient<IArtworkClient, ArtworkClient>(httpClient => httpClient.Timeout = ArtworkClient.Timeout)
            .ConfigurePrimaryHttpMessageHandler(UpstreamHttp.CreateHandler);

        builder.Services.AddOptions<ClientUpdateOptions>()
            .Bind(builder.Configuration.GetSection(ClientUpdateOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ClientUpdateOptions>, ClientUpdateOptionsValidator>();
        // The timeout covers the answer's headers; the store gives a build's body its own
        builder.Services.AddHttpClient(ClientUpdateStore.HttpClientName, httpClient => httpClient.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(UpstreamHttp.CreateHandler);
        builder.Services.AddSingleton<ClientUpdateStore>();
        builder.Services.AddScoped<IClientUpdateService, ClientUpdateService>();
    }

    private static void ConfigureLibrary(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<LibraryOptions>()
            .Bind(builder.Configuration.GetSection(LibraryOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<LibraryOptions>, LibraryOptionsValidator>();

        builder.Services.AddSingleton(services => new ServerFolders(
        [
            AppContext.BaseDirectory,
            services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            DatabaseFolder(services),
            services.GetRequiredService<IOptions<AuthOptions>>().Value.KeysPath,
            services.GetRequiredService<IOptions<TrickplayOptions>>().Value.Path,
            services.GetRequiredService<IOptions<MarkerOptions>>().Value.Path,
            services.GetRequiredService<IOptions<ImageCacheOptions>>().Value.Path,
            services.GetRequiredService<IOptions<ClientUpdateOptions>>().Value.Path
        ]));
        builder.Services.AddScoped<IMovieSync, MovieSync>();
        builder.Services.AddScoped<ISeriesSync, SeriesSync>();
        builder.Services.AddScoped<ILibrarySyncService, LibrarySyncService>();
        builder.Services.AddSingleton<SyncLock>();
        builder.Services.AddSingleton<LibrarySyncTrigger>();
        builder.Services.AddHostedService<LibrarySyncWorker>();
    }

    private static string DatabaseFolder(IServiceProvider services)
    {
        var sqlitePath = services.GetRequiredService<IOptions<DatabaseOptions>>().Value.SqlitePath;
        if (string.IsNullOrEmpty(sqlitePath))
        {
            return "";
        }

        var fullPath = Path.GetFullPath(sqlitePath);
        return Path.GetDirectoryName(fullPath) ?? "";
    }

    private static void ConfigureProbing(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ProbingOptions>()
            .Bind(builder.Configuration.GetSection(ProbingOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ProbingOptions>, ProbingOptionsValidator>();

        builder.Services.AddSingleton<IFfprobeRunner, FfprobeRunner>();
        builder.Services.AddSingleton<FfprobeStatus>();
        builder.Services.AddScoped<IProbeService, ProbeService>();
        builder.Services.AddHostedService<ProbeWorker>();
    }

    private static void ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SetupState>();

        // Protects the secrets the server must be able to read back, such as the signing secrets of outgoing webhooks.
        // The keys live next to the database in the config folder; without them those secrets are lost.
        var keys = builder.Configuration.GetSection(AuthOptions.SectionName)[nameof(AuthOptions.KeysPath)] ?? new AuthOptions().KeysPath;
        var keysFolder = new DirectoryInfo(Path.GetFullPath(keys));
        builder.Services.AddDataProtection()
            .SetApplicationName("akira")
            .PersistKeysToFileSystem(keysFolder);
        builder.Services.AddSingleton<PinAttemptLimiter>();

        builder.Services.AddAuthentication(AkiraClaims.Scheme)
            .AddScheme<AuthenticationSchemeOptions, AkiraAuthenticationHandler>(AkiraClaims.Scheme, null);

        var user = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(AkiraClaims.UserId).Build();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AkiraPolicies.User, user)
            .AddPolicy(AkiraPolicies.Admin, policy => policy.Combine(user).RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(AkiraPolicies.Device, policy => policy.RequireAuthenticatedUser().RequireClaim(AkiraClaims.DeviceId))
            .AddPolicy(AkiraPolicies.Client, policy => policy.RequireAuthenticatedUser())
            // An endpoint that says nothing about authorization needs a signed in user, so a new
            // endpoint is closed unless it is opened on purpose
            .SetFallbackPolicy(user);
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AkiraAuthorizationResultHandler>();

        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.PairingStart, context =>
                PerClientPerMinute(context, settings => settings.PairingStartsPerMinute));
            options.AddPolicy(RateLimitPolicies.Anonymous, context =>
                PerClientPerMinute(context, settings => settings.AnonymousRequestsPerMinute));

            // Generous: a season pack imports dozens of episodes within a minute, all from one Sonarr
            options.AddPolicy(RateLimitPolicies.Webhook, context => PerClientPerMinute(context, _ => 600));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Title = "Too many requests",
                        Detail = "Too many requests from this address, try again in a minute.",
                        Status = StatusCodes.Status429TooManyRequests
                    }
                });
            };
        });

        builder.Services.AddScoped<ISetupService, SetupService>();
        builder.Services.AddScoped<IPairingService, PairingService>();
        builder.Services.AddScoped<IProfileService, ProfileService>();
        builder.Services.AddScoped<IPinSignInService, PinSignInService>();
        builder.Services.AddScoped<IRemoteService, RemoteService>();
        builder.Services.AddScoped<CredentialCheck>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IDeviceService, DeviceService>();
        builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
        builder.Services.AddScoped<IBrowserSessionService, BrowserSessionService>();
    }

    private static void ConfigurePlayback(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<PlaybackOptions>()
            .Bind(builder.Configuration.GetSection(PlaybackOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PlaybackOptions>, PlaybackOptionsValidator>();

        builder.Services.AddScoped<ITrackPreferenceService, TrackPreferenceService>();
        builder.Services.AddScoped<IWatchStateImportService, WatchStateImportService>();
        builder.Services.AddScoped<IPlaybackService, PlaybackService>();

        builder.Services.AddOptions<TranscodingOptions>()
            .Bind(builder.Configuration.GetSection(TranscodingOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TranscodingOptions>, TranscodingOptionsValidator>();
        builder.Services.AddSingleton<IHlsTranscoder, HlsTranscoder>();
        builder.Services.AddSingleton<IKeyframeIndex, KeyframeIndex>();
        builder.Services.AddSingleton<IProgressiveTranscoder, ProgressiveTranscoder>();
        builder.Services.AddSingleton<HlsStreamCache>();
        builder.Services.AddScoped<IHlsService, HlsService>();
        builder.Services.AddScoped<INowPlayingService, NowPlayingService>();
        builder.Services.AddHostedService<TranscodeCleanupWorker>();

        builder.Services.AddOptions<TrickplayOptions>()
            .Bind(builder.Configuration.GetSection(TrickplayOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TrickplayOptions>, TrickplayOptionsValidator>();
        builder.Services.AddSingleton<ITrickplayGenerator, TrickplayGenerator>();
        builder.Services.AddScoped<ITrickplayService, TrickplayService>();
        builder.Services.AddHostedService<TrickplayWorker>();

        builder.Services.AddOptions<MarkerOptions>()
            .Bind(builder.Configuration.GetSection(MarkerOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<MarkerOptions>, MarkerOptionsValidator>();
        builder.Services.AddSingleton<IAudioFingerprinter, AudioFingerprinter>();
        builder.Services.AddScoped<IMarkerScanService, MarkerScanService>();
        builder.Services.AddHostedService<MarkerWorker>();
    }

    private static void ConfigureEvents(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<EventOptions>()
            .Bind(builder.Configuration.GetSection(EventOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<EventOptions>, EventOptionsValidator>();

        builder.Services.AddSingleton<EventHub>();
        builder.Services.AddSingleton<ProgressEvents>();
        builder.Services.AddSingleton<EventSignal>();
        builder.Services.AddScoped<IOutgoingWebhookService, OutgoingWebhookService>();
        builder.Services.AddScoped<IOutgoingWebhookDispatcher, OutgoingWebhookDispatcher>();
        builder.Services.AddHostedService<OutgoingWebhookWorker>();

        builder.Services.AddOptions<TraktOptions>()
            .Bind(builder.Configuration.GetSection(TraktOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TraktOptions>, TraktOptionsValidator>();
        builder.Services.AddHttpClient(TraktClient.HttpClientName, httpClient => httpClient.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(UpstreamHttp.CreateHandler);
        builder.Services.AddSingleton<ITraktClient, TraktClient>();
        builder.Services.AddSingleton<TraktPendingLinks>();
        builder.Services.AddScoped<TraktTokens>();
        builder.Services.AddScoped<ITraktAccountService, TraktAccountService>();
        builder.Services.AddScoped<ITraktSyncService, TraktSyncService>();
        builder.Services.AddHostedService<TraktWorker>();
        builder.Services.AddHttpClient(OutgoingWebhookDispatcher.HttpClientName, httpClient => httpClient.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(UpstreamHttp.CreateHandler);
        builder.Services.AddSingleton<IEventPublisher, EventPublisher>();
        builder.Services.AddSingleton<EventSocket>();
        builder.Services.AddScoped<IEventFeedService, EventFeedService>();
        builder.Services.AddScoped<ILibraryEventService, LibraryEventService>();
        builder.Services.AddHostedService<EventRetentionWorker>();

        builder.Services.AddSingleton<LibraryRefreshQueue>();
        builder.Services.AddSingleton<ProbeTrigger>();
        builder.Services.AddScoped<IWebhookService, WebhookService>();
        builder.Services.AddScoped<ILibraryRefreshService, LibraryRefreshService>();
        builder.Services.AddHostedService<LibraryRefreshWorker>();
    }

    private static void ConfigureSubtitles(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<SubtitleOptions>()
            .Bind(builder.Configuration.GetSection(SubtitleOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SubtitleOptions>, SubtitleOptionsValidator>();
        builder.Services.AddSingleton<ISubtitleExtractor, FfmpegSubtitleExtractor>();
        builder.Services.AddSingleton<ISubtitleConverter, SubtitleConverter>();
        builder.Services.AddSingleton<SubtitleRequestTrigger>();
        builder.Services.AddScoped<ISubtitleService, SubtitleService>();
        builder.Services.AddScoped<ISubtitleRequestProcessor, SubtitleRequestProcessor>();
        builder.Services.AddHostedService<SubtitleRequestWorker>();
    }

    private static void ConfigurePlugins(this WebApplicationBuilder builder, IMvcBuilder controllers)
    {
        // The subtitle sources and translators are plugins, OpenSubtitles and Lingarr built in
        var catalog = builder.Services.AddPlugins(builder.Configuration,
            [(typeof(OpenSubtitlesPlugin), OpenSubtitlesPlugin.Section), (typeof(LingarrPlugin), LingarrPlugin.Section)]);
        builder.Services.AddSingleton<IPluginService, PluginService>();

        // Only a switched on bridge brings its controllers, and the convention keeps them under its own path
        foreach (var assembly in catalog.ActiveBridges.Select(plugin => plugin.Assembly).Distinct())
        {
            controllers.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
        }

        // A plugin may register its bridge with a factory, so its class is only known once the container can make it
        builder.Services.AddOptions<MvcOptions>().Configure<PluginCatalog, IServiceProvider>((options, plugins, services) =>
        {
            var bridges = ActiveBridges(plugins, services);
            options.Conventions.Add(new BridgeControllerConvention(bridges, plugins.BridgeAssemblies(services)));
        });

        // What bridges reach Akira through
        builder.Services.AddScoped<IAccountAccess, AccountAccess>();
        builder.Services.AddSingleton<PairingStartLimiter>();
        builder.Services.AddScoped<ILibraryAccess, LibraryAccess>();
        builder.Services.AddScoped<IPlaybackAccess, PlaybackAccess>();
        builder.Services.AddScoped<IWatchStateAccess, WatchStateAccess>();
        builder.Services.AddScoped<IEventFeed, EventFeed>();
        builder.Services.AddScoped<IServerInfo, ServerInfo>();
        builder.Services.AddSingleton<SignedUrls>();
        builder.Services.AddSingleton<IMediaAccess>(provider => provider.GetRequiredService<SignedUrls>());
        builder.Services.AddScoped<ISignedUrlResolver, SignedUrlResolver>();
        builder.Services.AddScoped<IProgressiveService, ProgressiveService>();
        builder.Services.AddSingleton<DirectPlays>();
        builder.Services.AddScoped<IBridgeService, BridgeService>();
    }

    private static List<ActiveBridge> ActiveBridges(PluginCatalog plugins, IServiceProvider services)
    {
        var made = plugins.Bridges(services);
        return plugins.ActiveBridges
            .Where(plugin => made.ContainsKey(plugin.Key))
            .Select(plugin => new ActiveBridge(plugin.Key, made[plugin.Key].GetType(), plugin.Assembly))
            .ToList();
    }

    private static RateLimitPartition<string> PerClientPerMinute(HttpContext context, Func<AuthOptions, int> permits)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientAddresses.PartitionKey(context.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits(settings), Window = TimeSpan.FromMinutes(1) });
    }

    private static void ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IHealthService, HealthService>();
        builder.Services.AddScoped<ILibraryService, LibraryService>();
        builder.Services.AddScoped<IImageService, ImageService>();
        builder.Services.AddScoped<IImageDeliveryService, ImageDeliveryService>();
        builder.Services.AddOptions<ImageCacheOptions>()
            .Bind(builder.Configuration.GetSection(ImageCacheOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ImageCacheOptions>, ImageCacheOptionsValidator>();
        builder.Services.AddSingleton<ImageCache>();
        builder.Services.AddSingleton<IImageDecoder, ImageDecoder>();
        builder.Services.AddScoped<IImageHashService, ImageHashService>();
        builder.Services.AddHostedService<ImageHashWorker>();
        builder.Services.AddScoped<IFileService, FileService>();
        builder.Services.AddScoped<IRemovalSettingsService, RemovalSettingsService>();
        builder.Services.AddScoped<IActionService, ActionService>();
    }
}
