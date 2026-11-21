using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Integrations.Trakt;

public sealed class TraktOptionsValidator : IValidateOptions<TraktOptions>
{
    public ValidateOptionsResult Validate(string? name, TraktOptions options)
    {
        var prefix = TraktOptions.SectionName;
        var failures = new List<string>();

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || url.Scheme != "https")
        {
            failures.Add($"{prefix}:{nameof(TraktOptions.Url)} must be an absolute https url.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) != string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add($"{prefix} needs both {nameof(TraktOptions.ClientId)} and {nameof(TraktOptions.ClientSecret)}, or neither.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
