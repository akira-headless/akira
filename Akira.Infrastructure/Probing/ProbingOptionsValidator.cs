using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Probing;

public sealed class ProbingOptionsValidator : IValidateOptions<ProbingOptions>
{
    public ValidateOptionsResult Validate(string? name, ProbingOptions options)
    {
        var prefix = ProbingOptions.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FfprobePath))
        {
            failures.Add($"{prefix}:{nameof(ProbingOptions.FfprobePath)} is required.");
        }

        if (options.Timeout < TimeSpan.FromSeconds(5))
        {
            failures.Add($"{prefix}:{nameof(ProbingOptions.Timeout)} must be at least 5 seconds.");
        }

        if (options.Interval < TimeSpan.FromSeconds(10))
        {
            failures.Add($"{prefix}:{nameof(ProbingOptions.Interval)} must be at least 10 seconds.");
        }

        if (options.RescanInterval < options.Interval)
        {
            failures.Add($"{prefix}:{nameof(ProbingOptions.RescanInterval)} must be at least {prefix}:{nameof(ProbingOptions.Interval)}.");
        }

        if (options.MaxConcurrency is < 1 or > 16)
        {
            failures.Add($"{prefix}:{nameof(ProbingOptions.MaxConcurrency)} must be between 1 and 16.");
        }

        if (options.BatchSize is < 1 or > 500)
        {
            failures.Add($"{prefix}:{nameof(ProbingOptions.BatchSize)} must be between 1 and 500.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
