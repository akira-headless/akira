using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Transcoding;

public sealed class TranscodingOptionsValidator : IValidateOptions<TranscodingOptions>
{
    private static readonly HashSet<string> Presets =
        ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"];

    public ValidateOptionsResult Validate(string? name, TranscodingOptions options)
    {
        var prefix = TranscodingOptions.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.FfmpegPath))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.FfmpegPath)} is required.");
        }

        if (options.MaxConcurrent < 0)
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.MaxConcurrent)} cannot be negative, 0 turns transcoding off.");
        }

        if (options.SegmentLength < TimeSpan.FromSeconds(2) || options.SegmentLength > TimeSpan.FromSeconds(10))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.SegmentLength)} must be between 00:00:02 and 00:00:10.");
        }

        if (!Enum.IsDefined(options.HardwareAcceleration))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.HardwareAcceleration)} must be None, Nvenc, Qsv, Vaapi or Amf.");
        }

        if (!Enum.IsDefined(options.ToneMapping))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.ToneMapping)} must be Cpu, OpenCl or Vaapi.");
        }
        else if (options.ToneMapping == ToneMapping.OpenCl && options.HardwareAcceleration is HardwareAcceleration.None or HardwareAcceleration.Vaapi)
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.ToneMapping)} OpenCl needs {nameof(TranscodingOptions.HardwareAcceleration)} Nvenc, Qsv or Amf; with Vaapi use Vaapi.");
        }
        else if (options.ToneMapping == ToneMapping.Vaapi && options.HardwareAcceleration != HardwareAcceleration.Vaapi)
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.ToneMapping)} Vaapi needs {nameof(TranscodingOptions.HardwareAcceleration)} Vaapi.");
        }

        if (options.HardwareAcceleration == HardwareAcceleration.Vaapi && string.IsNullOrWhiteSpace(options.VaapiDevice))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.VaapiDevice)} is required for Vaapi.");
        }

        if (!Presets.Contains(options.Preset))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.Preset)} must be one of {string.Join(", ", Presets)}.");
        }

        if (options.MaxVideoBitrateKbps < 500)
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.MaxVideoBitrateKbps)} must be at least 500.");
        }

        if (options.IdleTimeout < TimeSpan.FromSeconds(30))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.IdleTimeout)} must be at least 00:00:30.");
        }

        if (options.SegmentTimeout < TimeSpan.FromSeconds(5))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.SegmentTimeout)} must be at least 00:00:05.");
        }

        // A request that waits for its segment does not count as asking again, so a shorter idle time stops the run it waits on
        if (options.IdleTimeout < options.SegmentTimeout)
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.IdleTimeout)} cannot be shorter than {nameof(TranscodingOptions.SegmentTimeout)}.");
        }

        if (options.MaxAhead < TimeSpan.FromSeconds(30))
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.MaxAhead)} must be at least 00:00:30.");
        }

        if (options.KeepBehind < TimeSpan.Zero)
        {
            failures.Add($"{prefix}:{nameof(TranscodingOptions.KeepBehind)} cannot be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
