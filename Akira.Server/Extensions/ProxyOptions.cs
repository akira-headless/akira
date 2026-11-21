namespace Akira.Server.Extensions;

public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    // Addresses or networks, such as 172.18.0.2 or 172.18.0.0/16
    public List<string> TrustedProxies { get; set; } = [];
}
