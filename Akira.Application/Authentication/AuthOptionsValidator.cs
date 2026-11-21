using Microsoft.Extensions.Options;

namespace Akira.Server.Authentication;

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var prefix = AuthOptions.SectionName;
        var failures = new List<string>();

        if (options.PairingLifetime < TimeSpan.FromMinutes(1) || options.PairingLifetime > TimeSpan.FromHours(1))
        {
            failures.Add($"{prefix}:{nameof(AuthOptions.PairingLifetime)} must be between 1 minute and 1 hour.");
        }

        if (options.BrowserSessionLifetime < TimeSpan.FromMinutes(5))
        {
            failures.Add($"{prefix}:{nameof(AuthOptions.BrowserSessionLifetime)} must be at least 5 minutes.");
        }

        if (options.ProfileSessionLifetime < TimeSpan.FromMinutes(5))
        {
            failures.Add($"{prefix}:{nameof(AuthOptions.ProfileSessionLifetime)} must be at least 5 minutes.");
        }

        if (options.PinMaxAttempts is < 1 or > 100)
        {
            failures.Add($"{prefix}:{nameof(AuthOptions.PinMaxAttempts)} must be between 1 and 100.");
        }

        if (options.PinLockout < TimeSpan.FromSeconds(10))
        {
            failures.Add($"{prefix}:{nameof(AuthOptions.PinLockout)} must be at least 10 seconds.");
        }

        if (options.PairingStartsPerMinute < 1 || options.AnonymousRequestsPerMinute < 1)
        {
            failures.Add($"{prefix}:{nameof(AuthOptions.PairingStartsPerMinute)} and " +
                         $"{prefix}:{nameof(AuthOptions.AnonymousRequestsPerMinute)} must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
