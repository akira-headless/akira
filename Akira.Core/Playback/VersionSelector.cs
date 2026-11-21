using Akira.Core.Enum;

namespace Akira.Core.Playback;

/// <summary>
/// Picks the version of an item a client should play.
/// </summary>
public static class VersionSelector
{
    public static VersionChoice? Choose(
        IReadOnlyList<VersionCandidate> versions,
        DeviceCapabilities capabilities,
        Guid? requestedFileId)
    {
        if (versions.Count == 0)
        {
            return null;
        }

        if (requestedFileId is { } requested && versions.FirstOrDefault(v => v.File.Id == requested) is { } chosen)
        {
            return new VersionChoice(chosen.File, "requested", IsCompatible(chosen, capabilities));
        }

        // A codec the client cannot decode is the hardest failure, an HDR format it cannot show plays
        // with wrong colours, and a resolution above its screen only wastes bandwidth. A file over the
        // client's bitrate has to be encoded, so a smaller copy that fits as it is goes first.
        var best = versions
            .OrderByDescending(v => capabilities.CanPlayVideo(v.Video?.Codec ?? v.File.Container))
            .ThenByDescending(v => HdrFits(v, capabilities))
            .ThenByDescending(v => capabilities.MaxHeight is not { } max || (v.Video?.Height ?? 0) <= max)
            .ThenByDescending(v => capabilities.MaxBitrateKbps is not { } cap || StreamDecision.Kbps(v.File) is not { } kbps || kbps <= cap)
            .ThenByDescending(v => v.Video?.Height ?? 0)
            .ThenByDescending(v => v.File.ProbeStatus == ProbeStatus.Probed)
            .ThenBy(v => v.InstanceOrder)
            .First();

        var reason = versions.Count == 1 ? "only-version" : "best-match";
        return new VersionChoice(best.File, reason, IsCompatible(best, capabilities));
    }

    private static bool IsCompatible(VersionCandidate version, DeviceCapabilities capabilities) =>
        version.Video is null || (capabilities.CanPlayVideo(version.Video.Codec) && HdrFits(version, capabilities));

    private static bool HdrFits(VersionCandidate version, DeviceCapabilities capabilities) =>
        Formats.Hdr(version.Video?.HdrFormat) is not { } hdr || capabilities.CanShowHdr(hdr);
}
