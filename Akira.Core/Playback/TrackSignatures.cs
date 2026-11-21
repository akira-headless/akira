using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Media;

namespace Akira.Core.Playback;

/// <summary>
/// Describes a track by what it is rather than by id, so a choice made on one file applies to the next.
/// </summary>
public static class TrackSignatures
{
    // language|codec class|flags|title. The codec class is lossless or lossy rather than the codec,
    // so a remembered "Japanese 2.0" still matches when an upgrade swaps FLAC for another lossless codec.
    public static string For(MediaStream stream) =>
        string.Join('|', stream.Language ?? string.Empty, CodecClass(stream), Flags(stream), StreamIds.NormalizeTitle(stream.Title));

    /// <summary>
    /// Returns the language and flags part, matched when the full signature does not, for example after an upgrade changed the codec.
    /// </summary>
    public static string LanguageAndFlags(string signature)
    {
        var parts = signature.Split('|');
        return parts.Length >= 3 ? $"{parts[0]}|{parts[2]}" : signature;
    }

    public static string LanguageAndFlags(MediaStream stream) => LanguageAndFlags(For(stream));

    private static string CodecClass(MediaStream stream) => stream.Kind switch
    {
        StreamKind.Audio => Formats.IsLossless(Formats.Audio(stream)) ? "lossless" : "lossy",
        StreamKind.Subtitle => stream.IsTextBased ? "text" : "image",
        _ => string.Empty
    };

    private static string Flags(MediaStream stream) => string.Concat(
        stream.IsForced ? "f" : string.Empty,
        stream.IsHearingImpaired ? "h" : string.Empty,
        stream.IsCommentary ? "c" : string.Empty,
        stream.IsDescriptive ? "d" : string.Empty);
}
