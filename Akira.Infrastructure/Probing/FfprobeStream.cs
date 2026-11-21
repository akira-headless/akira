namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents one stream of a file as ffprobe reports it. Numbers that ffprobe writes as text stay strings here.
/// </summary>
public sealed record FfprobeStream(
    int Index,
    string? CodecType,
    string? CodecName,
    string? Profile,
    int? Width,
    int? Height,
    string? PixFmt,
    string? BitsPerRawSample,
    string? ColorTransfer,
    string? AvgFrameRate,
    string? RFrameRate,
    int? Channels,
    string? ChannelLayout,
    string? NbFrames,
    string? NbReadPackets,
    Dictionary<string, int>? Disposition,
    Dictionary<string, string>? Tags,
    List<FfprobeSideData>? SideDataList);
