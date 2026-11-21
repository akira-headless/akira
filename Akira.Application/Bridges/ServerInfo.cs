using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Akira.Contracts.Accounts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Infrastructure.Data;

namespace Akira.Server.Bridges;

public sealed class ServerInfo : IServerInfo
{
    private static readonly SemaphoreSlim IdLock = new(1, 1);

    private readonly AkiraDbContext _dbContext;
    private readonly IServer _server;
    private readonly IOptions<AuthOptions> _authOptions;

    public ServerInfo(
        AkiraDbContext dbContext,
        IServer server,
        IOptions<AuthOptions> authOptions)
    {
        _dbContext = dbContext;
        _server = server;
        _authOptions = authOptions;
    }

    /// <inheritdoc />
    public IReadOnlyList<Uri> Addresses()
    {
        var listening = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var local = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address)
            .Distinct()
            .ToList();

        var addresses = new List<Uri>();
        foreach (var url in listening)
        {
            // Kestrel writes a wildcard as 0.0.0.0, [::] or +; those answer on every address of the machine
            var normalized = url
                .Replace("://+", "://0.0.0.0", StringComparison.Ordinal)
                .Replace("://*", "://0.0.0.0", StringComparison.Ordinal);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
            {
                continue;
            }

            if (uri.Host is "0.0.0.0" or "[::]" or "::")
            {
                addresses.AddRange(local.Select(ip => new Uri($"http://{ip}:{uri.Port}")));
            }
            else if (IPAddress.TryParse(uri.Host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                addresses.Add(new Uri($"http://{ip}:{uri.Port}"));
            }
        }

        return addresses.Distinct().ToList();
    }

    /// <inheritdoc />
    public async Task<Guid> ServerIdAsync(CancellationToken cancellationToken)
    {
        // Kept next to the key ring, which lives as long as the install: asked before setup or after the first user is
        // gone, it stays what it was, so bridge clients keep seeing the same server
        var path = Path.Combine(Path.GetFullPath(_authOptions.Value.KeysPath), "server-id");
        await IdLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                var storedText = await File.ReadAllTextAsync(path, cancellationToken);
                if (Guid.TryParse(storedText, out var stored))
                {
                    return stored;
                }
            }

            // An install from before the file starts with the id it had, derived from its first user
            var firstUserId = await _dbContext.Users
                .AsNoTracking()
                .OrderBy(user => user.CreatedAt)
                .Select(user => (Guid?)user.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var id = firstUserId is { } userId ? IdFromUser(userId) : Guid.NewGuid();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, id.ToString(), cancellationToken);
            return id;
        }
        finally
        {
            IdLock.Release();
        }
    }

    private static Guid IdFromUser(Guid userId)
    {
        var seed = Encoding.UTF8.GetBytes($"akira-server:{userId:N}");
        var hash = SHA256.HashData(seed);
        return new Guid(hash.AsSpan(0, 16));
    }
}
