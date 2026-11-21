using Microsoft.Extensions.Options;

namespace Akira.Server.Events;

public sealed class EventOptionsValidator : IValidateOptions<EventOptions>
{
    public ValidateOptionsResult Validate(string? name, EventOptions options) =>
        options.Retention < TimeSpan.FromHours(1)
            ? ValidateOptionsResult.Fail($"{EventOptions.SectionName}:{nameof(EventOptions.Retention)} must be at least 01:00:00.")
            : ValidateOptionsResult.Success;
}
