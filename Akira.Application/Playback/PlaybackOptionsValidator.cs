using Microsoft.Extensions.Options;
using Akira.Core.Media;
using Akira.Infrastructure.Sync;

namespace Akira.Server.Playback;

public sealed class PlaybackOptionsValidator : IValidateOptions<PlaybackOptions>
{
    public ValidateOptionsResult Validate(string? name, PlaybackOptions options)
    {
        var prefix = PlaybackOptions.SectionName;
        var failures = new List<string>();

        if (options.PlayedThreshold is < 0.5 or > 1)
        {
            failures.Add($"{prefix}:{nameof(PlaybackOptions.PlayedThreshold)} must be between 0.5 and 1.");
        }

        if (options.MinimumResume < TimeSpan.Zero)
        {
            failures.Add($"{prefix}:{nameof(PlaybackOptions.MinimumResume)} cannot be negative.");
        }

        var languageLists = new[]
        {
            (nameof(LanguageDefaults.Subtitles), options.Languages.Subtitles),
            (nameof(LanguageDefaults.Audio), options.Languages.Audio)
        };
        foreach (var (field, list) in languageLists)
        {
            var unknown = LanguageDefaults.Split(list).Where(code => Languages.Normalize(code) is null).ToList();
            if (unknown.Count > 0)
            {
                failures.Add($"{prefix}:{nameof(PlaybackOptions.Languages)}:{field} has codes that are no language: {string.Join(", ", unknown)}. " +
                             "Use ISO 639-1 or 639-2, for example nl,en.");
            }
        }

        if (options.Languages.SubtitleMode is { } mode && !Enum.IsDefined(mode))
        {
            failures.Add($"{prefix}:{nameof(PlaybackOptions.Languages)}:{nameof(LanguageDefaults.SubtitleMode)} must be Off, ForcedOnly, " +
                         "WhenAudioForeign or Always.");
        }

        for (var index = 0; index < options.ClientPathMappings.Count; index++)
        {
            var mapping = options.ClientPathMappings[index];
            if (string.IsNullOrWhiteSpace(mapping.From) || string.IsNullOrWhiteSpace(mapping.To))
            {
                failures.Add($"{prefix}:{nameof(PlaybackOptions.ClientPathMappings)}:{index} needs both " +
                             $"{nameof(PathMappingOptions.From)} and {nameof(PathMappingOptions.To)}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
