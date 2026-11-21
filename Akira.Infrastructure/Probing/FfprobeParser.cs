using System.Globalization;
using System.Text.RegularExpressions;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Media;

namespace Akira.Infrastructure.Probing;

/// <summary>
/// Turns ffprobe output into tracks with stable ids and chapters.
/// </summary>
public static partial class FfprobeParser
{
    // Raise this when the parsing changes, every file is probed again on the next pass
    public const int Version = 1;

    public const int ExternalSortOrderStart = 1000;

    public static ProbedMedia Parse(FfprobeOutput output, Guid fileId)
    {
        var streams = new List<MediaStream>();
        var ordinals = new Dictionary<(StreamKind, string), int>();

        foreach (var stream in output.Streams ?? [])
        {
            var kind = stream.CodecType switch
            {
                "video" when Disposition(stream, "attached_pic") => (StreamKind?)null,
                "video" => StreamKind.Video,
                "audio" => StreamKind.Audio,
                "subtitle" => StreamKind.Subtitle,
                _ => null
            };

            if (kind is null)
            {
                continue;
            }

            var parsed = ToStream(stream, kind.Value);
            var signature = StreamIds.Signature(parsed.Language, parsed.Codec, parsed.Channels, parsed.IsForced,
                parsed.IsHearingImpaired, parsed.IsCommentary, parsed.IsDescriptive, parsed.Title);
            var ordinal = ordinals.GetValueOrDefault((kind.Value, signature));
            ordinals[(kind.Value, signature)] = ordinal + 1;

            parsed.Id = StreamIds.Embedded(fileId, kind.Value, signature, ordinal);
            parsed.MediaFileId = fileId;
            parsed.SortOrder = streams.Count;
            parsed.IndexHint = stream.Index;
            streams.Add(parsed);
        }

        var chapters = (output.Chapters ?? [])
            .Select((chapter, order) => new MediaChapter
            {
                MediaFileId = fileId,
                SortOrder = order,
                Title = NullIfEmpty(chapter.Tags?.GetValueOrDefault("title")),
                StartMs = ToMilliseconds(chapter.StartTime) ?? 0,
                EndMs = ToMilliseconds(chapter.EndTime)
            })
            .ToList();

        return new ProbedMedia(ToMilliseconds(output.Format?.Duration), streams, chapters);
    }

    /// <summary>
    /// Builds the track for an external subtitle file from its ffprobe output and what its name says.
    /// </summary>
    public static MediaStream? ParseExternalSubtitle(
        FfprobeOutput output,
        Guid fileId,
        string fileName,
        string fullPath,
        ExternalSubtitleName name,
        int sortOrder)
    {
        var stream = output.Streams?.FirstOrDefault(candidate => candidate.CodecType == "subtitle");
        if (stream is null)
        {
            return null;
        }

        var parsed = ToStream(stream, StreamKind.Subtitle);
        parsed.Id = StreamIds.External(fileId, fileName);
        parsed.MediaFileId = fileId;
        parsed.SortOrder = sortOrder;
        parsed.IsExternal = true;
        parsed.ExternalPath = fullPath;
        parsed.IndexHint = null;

        // The file name is all there is to know about an external subtitle
        parsed.Language = name.Language ?? parsed.Language;
        parsed.Title = name.Title ?? parsed.Title;
        parsed.IsForced |= name.IsForced;
        parsed.IsHearingImpaired |= name.IsHearingImpaired;
        parsed.IsDefault |= name.IsDefault;
        return parsed;
    }

    private static MediaStream ToStream(FfprobeStream stream, StreamKind kind)
    {
        var title = NullIfEmpty(stream.Tags?.GetValueOrDefault("title"));
        var parsed = new MediaStream
        {
            Id = string.Empty,
            Kind = kind,
            Codec = NullIfEmpty(stream.CodecName)?.ToLowerInvariant(),
            Profile = NullIfEmpty(stream.Profile),
            Language = Languages.Normalize(stream.Tags?.GetValueOrDefault("language")),
            Title = title,
            IsDefault = Disposition(stream, "default"),
            IsForced = Disposition(stream, "forced") || TrackTitles.IsForced(title),
            IsHearingImpaired = Disposition(stream, "hearing_impaired") || TrackTitles.IsHearingImpaired(title),
            IsCommentary = Disposition(stream, "comment") || TrackTitles.IsCommentary(title),
            IsDescriptive = Disposition(stream, "visual_impaired") || TrackTitles.IsDescriptive(title)
        };

        switch (kind)
        {
            case StreamKind.Video:
                parsed.Width = stream.Width;
                parsed.Height = stream.Height;
                parsed.BitDepth = BitDepth(stream);
                parsed.FrameRate = FrameRate(stream.AvgFrameRate) ?? FrameRate(stream.RFrameRate);
                parsed.HdrFormat = Hdr(stream);
                break;
            case StreamKind.Audio:
                parsed.Channels = stream.Channels;
                parsed.ChannelLayout = NullIfEmpty(stream.ChannelLayout);
                parsed.BitDepth = ParseInt(stream.BitsPerRawSample) is > 0 and var bits ? bits : null;
                break;
            case StreamKind.Subtitle:
                parsed.IsTextBased = SubtitleFiles.IsTextBased(parsed.Codec);
                parsed.EventCount = EventCount(stream);
                break;
        }

        return parsed;
    }

    // mkvmerge writes NUMBER_OF_FRAMES statistics per track, MP4 knows its sample count, and an
    // external file is counted packet by packet. None of them needs the whole file to be read.
    private static int? EventCount(FfprobeStream stream)
    {
        var statistics = stream.Tags?
            .FirstOrDefault(tag => tag.Key.StartsWith("NUMBER_OF_FRAMES", StringComparison.OrdinalIgnoreCase)).Value;

        return ParseInt(statistics) ?? ParseInt(stream.NbReadPackets) ?? ParseInt(stream.NbFrames);
    }

    private static HdrFormat Hdr(FfprobeStream stream)
    {
        if (IsDolbyVision(stream))
        {
            return HdrFormat.DolbyVision;
        }

        // HDR10+ metadata only exists per frame, so it reads as HDR10 here
        return stream.ColorTransfer switch
        {
            "smpte2084" => HdrFormat.Hdr10,
            "arib-std-b67" => HdrFormat.Hlg,
            _ => HdrFormat.Sdr
        };
    }

    private static bool IsDolbyVision(FfprobeStream stream)
    {
        if (stream.SideDataList is null)
        {
            return false;
        }

        return stream.SideDataList.Any(data => data.SideDataType?.Contains("DOVI", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static int? BitDepth(FfprobeStream stream)
    {
        if (ParseInt(stream.BitsPerRawSample) is > 0 and var bits)
        {
            return bits;
        }

        if (stream.PixFmt is null)
        {
            return null;
        }

        var match = PixelFormatDepth().Match(stream.PixFmt);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 8;
    }

    private static double? FrameRate(string? rate)
    {
        if (string.IsNullOrEmpty(rate))
        {
            return null;
        }

        var parts = rate.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && numerator > 0 && denominator > 0)
        {
            return Math.Round(numerator / denominator, 3);
        }

        return null;
    }

    private static long? ToMilliseconds(string? seconds) =>
        double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? (long)Math.Round(value * 1000)
            : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static bool Disposition(FfprobeStream stream, string name) =>
        stream.Disposition?.GetValueOrDefault(name) == 1;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"p(\d{1,2})(?:le|be)$")]
    private static partial Regex PixelFormatDepth();
}
