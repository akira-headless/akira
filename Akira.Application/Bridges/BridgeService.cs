using System.Security.Claims;
using Akira.Contracts.Bridges;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Models.Auth;
using Akira.Server.Plugins;
using Akira.Server.Services.Auth;
using Akira.Server.Services;

namespace Akira.Server.Bridges;

public sealed class BridgeService : IBridgeService
{
    public const string BasePath = "/bridges";

    private readonly PluginCatalog _catalog;
    private readonly IServiceProvider _services;
    private readonly AkiraDbContext _dbContext;
    private readonly ILogger<BridgeService> _logger;

    public BridgeService(
        PluginCatalog catalog,
        IServiceProvider services,
        AkiraDbContext dbContext,
        ILogger<BridgeService> logger)
    {
        _catalog = catalog;
        _services = services;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<BridgeSummary> List(Uri serverBase) =>
        ActiveBridges().Select(plugin =>
            {
                var connect = Bridge(plugin.Key).Connect;
                var url = BaseUrl(serverBase, plugin.Key).ToString().TrimEnd('/');
                return new BridgeSummary(plugin.Key, plugin.Manifest.Name, plugin.Manifest.Description, Kind(connect.Kind),
                    connect.Instructions, url);
            })
            .ToList();

    /// <inheritdoc />
    public async Task<ServiceResult<LinkCreated>> CreateLinkAsync(ClaimsPrincipal principal, CreateLinkRequest request, Uri serverBase,
        CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } userId)
        {
            return ServiceResult<LinkCreated>.Fail(ServiceError.Forbidden, "Pick a profile first, a link belongs to a user.");
        }

        if (principal.IsProfileSession())
        {
            return ServiceResult<LinkCreated>.Fail(ServiceError.Forbidden, PairingService.ProfileSessionRefusal);
        }

        var plugin = ActiveBridges().FirstOrDefault(p => p.Key == request.Bridge);
        if (plugin is null)
        {
            return ServiceResult<LinkCreated>.Fail(ServiceError.NotFound, $"No bridge {request.Bridge} is switched on.");
        }

        var bridge = Bridge(plugin.Key);
        if (bridge.Connect.Kind != ConnectKind.Link)
        {
            return ServiceResult<LinkCreated>.Fail(ServiceError.Invalid, $"{plugin.Manifest.Name} does not use links.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? plugin.Manifest.Name : request.Name.Trim();
        if (name.Length > 100)
        {
            return ServiceResult<LinkCreated>.Fail(ServiceError.Invalid, "name is at most 100 characters.");
        }

        var token = Secrets.NewLinkToken();
        if (bridge.Link(BaseUrl(serverBase, plugin.Key), token) is not { } link)
        {
            return ServiceResult<LinkCreated>.Fail(ServiceError.Invalid, $"{plugin.Manifest.Name} did not make a link url.");
        }

        var device = new Device { Name = name, Client = plugin.Key, Kind = DeviceKind.Link, UserId = userId };
        _dbContext.Devices.Add(device);
        _dbContext.DeviceTokens.Add(new DeviceToken { DeviceId = device.Id, UserId = userId, TokenHash = Secrets.Hash(token) });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dbContext.Entry(device).Reference(d => d.User).LoadAsync(cancellationToken);

        _logger.LogInformation("{User} made a {Bridge} link {Name}", principal.Identity?.Name, plugin.Key, name);
        return ServiceResult<LinkCreated>.Ok(new LinkCreated(device.ToSummary(), plugin.Key, token, link.Url, link.AppUrl));
    }

    private static BridgeConnectKind Kind(ConnectKind kind) => kind switch
    {
        ConnectKind.Link => BridgeConnectKind.Link,
        ConnectKind.Address => BridgeConnectKind.Address,
        _ => BridgeConnectKind.Discovery
    };

    private static Uri BaseUrl(Uri serverBase, string key) => new(serverBase, $"{BasePath.TrimStart('/')}/{key}/");

    // Only the bridges that could be made
    private List<LoadedPlugin> ActiveBridges()
    {
        var made = _catalog.Bridges(_services);
        return _catalog.ActiveBridges.Where(plugin => made.ContainsKey(plugin.Key)).ToList();
    }

    private IClientBridge Bridge(string key) => _catalog.Bridges(_services)[key];
}
