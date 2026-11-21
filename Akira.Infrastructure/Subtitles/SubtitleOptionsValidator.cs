using Microsoft.Extensions.Options;
using Akira.Infrastructure.Sync;
using Akira.Infrastructure.Transcoding;

namespace Akira.Infrastructure.Subtitles;

public sealed class SubtitleOptionsValidator : IValidateOptions<SubtitleOptions>
{
    public ValidateOptionsResult Validate(string? name, SubtitleOptions options)
    {
        var prefix = SubtitleOptions.SectionName;
        var failures = new List<string>();

        if (!IsHttpUrl(options.OpenSubtitles.Url))
        {
            failures.Add($"{prefix}:{nameof(SubtitleOptions.OpenSubtitles)}:{nameof(OpenSubtitlesOptions.Url)} must be an absolute http or https url.");
        }

        if (string.IsNullOrWhiteSpace(options.OpenSubtitles.Username) != string.IsNullOrWhiteSpace(options.OpenSubtitles.Password))
        {
            failures.Add($"{prefix}:{nameof(SubtitleOptions.OpenSubtitles)} needs both {nameof(OpenSubtitlesOptions.Username)} and " +
                         $"{nameof(OpenSubtitlesOptions.Password)}, or neither.");
        }

        var lingarr = options.Lingarr;
        if ((!string.IsNullOrWhiteSpace(lingarr.Url) || !string.IsNullOrWhiteSpace(lingarr.ApiKey)) && !IsHttpUrl(lingarr.Url))
        {
            failures.Add($"{prefix}:{nameof(SubtitleOptions.Lingarr)}:{nameof(LingarrOptions.Url)} must be an absolute http or https url, " +
                         "for example http://lingarr:9876.");
        }

        for (var index = 0; index < lingarr.PathMappings.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lingarr.PathMappings[index].From) || string.IsNullOrWhiteSpace(lingarr.PathMappings[index].To))
            {
                failures.Add($"{prefix}:{nameof(SubtitleOptions.Lingarr)}:{nameof(LingarrOptions.PathMappings)}:{index} needs both " +
                             $"{nameof(PathMappingOptions.From)} and {nameof(PathMappingOptions.To)}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.FfmpegPath))
        {
            failures.Add($"{prefix}:{nameof(SubtitleOptions.FfmpegPath)} is no longer read. Set {TranscodingOptions.SectionName}:" +
                         $"{nameof(TranscodingOptions.FfmpegPath)}, the one ffmpeg path of the server, and remove this setting.");
        }

        if (options.PollInterval < TimeSpan.FromSeconds(1))
        {
            failures.Add($"{prefix}:{nameof(SubtitleOptions.PollInterval)} must be at least 00:00:01.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https";
}
