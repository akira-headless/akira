using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Sync;

public sealed class LibraryOptionsValidator : IValidateOptions<LibraryOptions>
{
    public ValidateOptionsResult Validate(string? name, LibraryOptions options)
    {
        if (options.SyncInterval < LibraryOptions.MinimumSyncInterval)
        {
            return ValidateOptionsResult.Fail($"{LibraryOptions.SectionName}:{nameof(LibraryOptions.SyncInterval)} must be at least " +
                                              $"{LibraryOptions.MinimumSyncInterval}.");
        }

        return ValidateOptionsResult.Success;
    }
}
