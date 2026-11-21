using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Markers;

public sealed class MarkerOptionsValidator : IValidateOptions<MarkerOptions>
{
    public ValidateOptionsResult Validate(string? name, MarkerOptions options)
    {
        var prefix = MarkerOptions.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            failures.Add($"{prefix}:{nameof(MarkerOptions.Path)} is required.");
        }

        if (options.IntroWindow < TimeSpan.FromMinutes(1) || options.IntroWindow > TimeSpan.FromMinutes(30))
        {
            failures.Add($"{prefix}:{nameof(MarkerOptions.IntroWindow)} must be between 00:01:00 and 00:30:00.");
        }

        if (options.CreditsWindow < TimeSpan.FromMinutes(1) || options.CreditsWindow > TimeSpan.FromMinutes(15))
        {
            failures.Add($"{prefix}:{nameof(MarkerOptions.CreditsWindow)} must be between 00:01:00 and 00:15:00.");
        }

        if (options.MinLength < TimeSpan.FromSeconds(5) || options.MinLength >= options.MaxIntro)
        {
            failures.Add($"{prefix}:{nameof(MarkerOptions.MinLength)} must be at least 00:00:05 and shorter than {nameof(MarkerOptions.MaxIntro)}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
