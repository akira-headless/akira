using System.Net;
using System.Net.Sockets;

namespace Akira.Server.Authentication;

public static class ClientAddresses
{
    private static readonly string[] ForwardingHeaders = ["X-Forwarded-For", "X-Original-For", "Forwarded"];

    /// <summary>
    /// Returns what a rate limit counts as one client: the address, or for IPv6 its /64, which one household holds whole.
    /// </summary>
    public static string PartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (plain.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return plain.ToString();
        }

        var bytes = plain.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>
    /// Checks whether a request comes straight from the local network: from loopback or a private, link-local or unique
    /// local address, not passed on by a proxy, which would make an outside caller look local, and asked for by address.
    /// </summary>
    public static bool IsLocal(HttpContext context)
    {
        if (ForwardingHeaders.Any(header => context.Request.Headers.ContainsKey(header)))
        {
            return false;
        }

        return IsLocalHost(context.Request.Host) && context.Connection.RemoteIpAddress is { } address && IsLocal(address);
    }

    // A name can be pointed at this server by a page on the internet, whose scripts then read what answers here from the
    // browser of someone at home (DNS rebinding), and a proxy that adds no forwarding header still asks for the name it
    // serves. An address of the local network is neither. A renderer finds the address by itself, and an old one sends
    // no Host at all, which a browser and a proxy always do.
    private static bool IsLocalHost(HostString host) =>
        !host.HasValue || (IPAddress.TryParse(host.Host.Trim('[', ']'), out var address) && IsLocal(address));

    /// <summary>
    /// Checks whether the person behind a request is at home: on the local network directly, or through a proxy listed in
    /// Proxy:TrustedProxies, whose forwarding has then been applied and names a local client. A forwarding header still
    /// there came from a proxy that is not trusted, which could be passing on anyone.
    /// </summary>
    public static bool IsAtHome(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("Forwarded"))
        {
            return false;
        }

        return context.Connection.RemoteIpAddress is { } address && IsLocal(address);
    }

    public static bool IsLocal(IPAddress address)
    {
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(plain))
        {
            return true;
        }

        var bytes = plain.GetAddressBytes();
        if (plain.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254);
        }

        return plain.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
    }
}
