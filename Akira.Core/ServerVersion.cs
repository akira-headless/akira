using System.Reflection;

namespace Akira.Core;

public static class ServerVersion
{
    public static readonly string Number = Parse(
        typeof(ServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    public static string Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0";
        }

        // The SDK appends "+<commit sha>" to the informational version when building from a git checkout
        var buildMetadataStart = informationalVersion.IndexOf('+');
        if (buildMetadataStart < 0)
        {
            return informationalVersion;
        }

        return informationalVersion[..buildMetadataStart];
    }
}
