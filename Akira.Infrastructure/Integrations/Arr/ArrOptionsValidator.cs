using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Akira.Core.Library;
using Akira.Infrastructure.Sync;

namespace Akira.Infrastructure.Integrations.Arr;

public sealed partial class ArrOptionsValidator : IValidateOptions<ArrOptions>
{
    public const int MaxKeyLength = 32;
    public const int MinWebhookSecretLength = 16;

    public ValidateOptionsResult Validate(string? name, ArrOptions options)
    {
        var failures = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < options.Instances.Count; index++)
        {
            var instance = options.Instances[index];
            var prefix = $"{ArrOptions.SectionName}:{nameof(ArrOptions.Instances)}:{index}";

            if (!InstanceKey().IsMatch(instance.Key))
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.Key)} must be 1 to {MaxKeyLength} lowercase letters, " +
                             "digits or dashes, for example radarr-4k.");
            }
            else if (IdentityKeys.ReservedInstanceKeys.Contains(instance.Key))
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.Key)} cannot be '{instance.Key}': tmdb and tvdb are how items are " +
                             "identified. Use for example radarr or sonarr.");
            }
            else if (!keys.Add(instance.Key))
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.Key)} '{instance.Key}' is used by another instance.");
            }

            if (!Enum.IsDefined(instance.Kind))
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.Kind)} must be Radarr or Sonarr.");
            }

            if (!Uri.TryCreate(instance.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.Url)} must be an absolute http or https url, " +
                             "for example http://radarr:7878.");
            }

            if (string.IsNullOrWhiteSpace(instance.ApiKey))
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.ApiKey)} is required.");
            }

            if (!string.IsNullOrEmpty(instance.WebhookSecret) && instance.WebhookSecret.Length < MinWebhookSecretLength)
            {
                failures.Add($"{prefix}:{nameof(ArrInstanceOptions.WebhookSecret)} must be at least {MinWebhookSecretLength} characters, " +
                             "for example the output of openssl rand -hex 24.");
            }

            for (var mapping = 0; mapping < instance.PathMappings.Count; mapping++)
            {
                if (string.IsNullOrWhiteSpace(instance.PathMappings[mapping].From)
                    || string.IsNullOrWhiteSpace(instance.PathMappings[mapping].To))
                {
                    failures.Add($"{prefix}:{nameof(ArrInstanceOptions.PathMappings)}:{mapping} needs both " +
                                 $"{nameof(PathMappingOptions.From)} and {nameof(PathMappingOptions.To)}.");
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$")]
    private static partial Regex InstanceKey();
}
