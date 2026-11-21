using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Trickplay;

public sealed class TrickplayOptionsValidator : IValidateOptions<TrickplayOptions>
{
    public ValidateOptionsResult Validate(string? name, TrickplayOptions options)
    {
        var prefix = TrickplayOptions.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            failures.Add($"{prefix}:{nameof(TrickplayOptions.Path)} is required.");
        }

        if (options.Interval < TimeSpan.FromSeconds(1) || options.Interval > TimeSpan.FromMinutes(1))
        {
            failures.Add($"{prefix}:{nameof(TrickplayOptions.Interval)} must be between 00:00:01 and 00:01:00.");
        }

        if (options.Width is < 120 or > 640 || options.Width % 2 != 0)
        {
            failures.Add($"{prefix}:{nameof(TrickplayOptions.Width)} must be an even number between 120 and 640.");
        }

        if (options.Columns is < 1 or > 20 || options.Rows is < 1 or > 20)
        {
            failures.Add($"{prefix}:{nameof(TrickplayOptions.Columns)} and {nameof(TrickplayOptions.Rows)} must be between 1 and 20.");
        }

        if (options.Timeout < TimeSpan.FromMinutes(1))
        {
            failures.Add($"{prefix}:{nameof(TrickplayOptions.Timeout)} must be at least 00:01:00.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
