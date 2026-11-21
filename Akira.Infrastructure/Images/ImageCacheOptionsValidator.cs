using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Images;

public sealed class ImageCacheOptionsValidator : IValidateOptions<ImageCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, ImageCacheOptions options)
    {
        var prefix = ImageCacheOptions.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            failures.Add($"{prefix}:{nameof(ImageCacheOptions.Path)} is required.");
        }

        if (options.MaxMegabytes is < 0 or > 100_000)
        {
            failures.Add($"{prefix}:{nameof(ImageCacheOptions.MaxMegabytes)} must be between 0 (off) and 100000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
