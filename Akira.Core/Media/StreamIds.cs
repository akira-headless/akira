using System.Security.Cryptography;
using System.Text;
using Akira.Core.Enum;

namespace Akira.Core.Media;

/// <summary>
/// Builds the stable ids of a file's tracks, so they survive rescans and never depend on stream order alone.
/// </summary>
public static class StreamIds
{
    // ffprobe does not expose the Matroska TrackUID, so embedded tracks are identified by what
    // they are, plus their position among tracks that are otherwise identical. The file id is part
    // of every id: a replaced file gets new ids, while remembered selections are stored as
    // attributes and are applied to the new file again.
    public static string Embedded(Guid fileId, StreamKind kind, string signature, int ordinalAmongIdentical) =>
        Hash($"{fileId:N}|{kind}|{signature}|{ordinalAmongIdentical}");

    public static string External(Guid fileId, string fileName) => Hash($"{fileId:N}|ext|{fileName}");

    public static string Signature(
        string? language,
        string? codec,
        int? channels,
        bool isForced,
        bool isHearingImpaired,
        bool isCommentary,
        bool isDescriptive,
        string? title) =>
        string.Join('|',
            language ?? string.Empty,
            codec?.ToLowerInvariant() ?? string.Empty,
            channels?.ToString() ?? string.Empty,
            isForced ? "f" : string.Empty,
            isHearingImpaired ? "hi" : string.Empty,
            isCommentary ? "c" : string.Empty,
            isDescriptive ? "ad" : string.Empty,
            NormalizeTitle(title));

    public static string NormalizeTitle(string? title) =>
        string.Join(' ', (title ?? string.Empty).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Hash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return hash[..32];
    }
}
