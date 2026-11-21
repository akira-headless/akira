using System.Net;
using Microsoft.Extensions.Options;

namespace Akira.Server.Extensions;

public sealed class ProxyOptionsValidator : IValidateOptions<ProxyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProxyOptions options)
    {
        var wrong = options.TrustedProxies.Where(entry => !IPAddress.TryParse(entry, out _) && !IPNetwork.TryParse(entry, out _)).ToList();
        if (wrong.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail($"{ProxyOptions.SectionName}:{nameof(ProxyOptions.TrustedProxies)} has entries that are no address or network: " +
                                          string.Join(", ", wrong));
    }
}
