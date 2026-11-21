using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Updates;

public sealed partial class ClientUpdateOptionsValidator : IValidateOptions<ClientUpdateOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientUpdateOptions options)
    {
        var prefix = ClientUpdateOptions.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            failures.Add($"{prefix}:{nameof(ClientUpdateOptions.Path)} is required.");
        }

        foreach (var (platform, source) in options.Sources)
        {
            // The platform is also the name of its folder
            if (!PlatformPattern().IsMatch(platform))
            {
                failures.Add($"{prefix}:Sources:{platform} must be lowercase letters, digits and dashes, at most 32.");
            }

            if (!Uri.TryCreate(source.ManifestUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add($"{prefix}:Sources:{platform}:{nameof(ClientUpdateSource.ManifestUrl)} must be an http or https url.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    public static partial Regex PlatformPattern();
}
