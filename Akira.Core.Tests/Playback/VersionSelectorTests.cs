using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class VersionSelectorTests
{
    private static readonly VersionCandidate Uhd = Version("4k", "hevc", 2160, HdrFormat.Hdr10, 1);
    private static readonly VersionCandidate DolbyVision = Version("dv", "hevc", 2160, HdrFormat.DolbyVision, 2);
    private static readonly VersionCandidate FullHd = Version("1080", "h264", 1080, HdrFormat.Sdr, 0);

    [Fact]
    public void CapableTv_GetsTheBestVersion()
    {
        var choice = VersionSelector.Choose([FullHd, Uhd], new DeviceCapabilities(VideoCodecs: ["h264", "hevc"], Hdr: ["hdr10"]), null)!;

        Assert.Equal(("4k", "best-match", true), (choice.File.InstanceKey, choice.Reason, choice.IsCompatible));
    }

    [Fact]
    public void SdrScreen_GetsTheSdrVersionInsteadOfWashedOutHdr()
    {
        var choice = VersionSelector.Choose([Uhd, FullHd], new DeviceCapabilities(VideoCodecs: ["h264", "hevc"], Hdr: []), null)!;

        Assert.Equal("1080", choice.File.InstanceKey);
    }

    [Fact]
    public void ClientWithoutHevc_GetsH264()
    {
        Assert.Equal("1080", VersionSelector.Choose([Uhd, FullHd], new DeviceCapabilities(VideoCodecs: ["h264"]), null)!.File.InstanceKey);
    }

    [Fact]
    public void DolbyVision_NeedsDolbyVisionSupport()
    {
        var choice = VersionSelector.Choose([DolbyVision, Uhd], new DeviceCapabilities(Hdr: ["hdr10"]), null)!;

        Assert.Equal("4k", choice.File.InstanceKey);
    }

    [Fact]
    public void MaxHeight_KeepsA1080pScreenOn1080p()
    {
        Assert.Equal("1080", VersionSelector.Choose([Uhd, FullHd], new DeviceCapabilities(MaxHeight: 1080), null)!.File.InstanceKey);
    }

    [Fact]
    public void ABitrateCap_GetsTheCopyThatFitsAsItIs_NotTheRemuxThatMustBeEncoded()
    {
        // A two hour film: the 4K remux is 60 GB (about 66 Mbit/s), the 1080p copy 6 GB
        var remux = Version("4k", "hevc", 2160, HdrFormat.Sdr, 0, sizeBytes: 60_000_000_000);
        var copy = Version("1080", "h264", 1080, HdrFormat.Sdr, 1, sizeBytes: 6_000_000_000);

        var away = VersionSelector.Choose([remux, copy], new DeviceCapabilities(MaxBitrateKbps: 8_000), null)!;
        var slow = VersionSelector.Choose([remux, copy], new DeviceCapabilities(MaxBitrateKbps: 2_000), null)!;

        Assert.Equal("1080", away.File.InstanceKey);
        // When nothing fits, the best source is encoded
        Assert.Equal("4k", slow.File.InstanceKey);
        Assert.Equal("4k", VersionSelector.Choose([remux, copy], DeviceCapabilities.Any, null)!.File.InstanceKey);
    }

    [Fact]
    public void RequestedVersion_IsUsedEvenWhenItIsNotTheBest_AndSaysIfItFits()
    {
        var choice = VersionSelector.Choose([Uhd, FullHd], new DeviceCapabilities(VideoCodecs: ["h264"]), Uhd.File.Id)!;

        Assert.Equal(("4k", "requested", false), (choice.File.InstanceKey, choice.Reason, choice.IsCompatible));
    }

    [Fact]
    public void Unprobed_FallsBackToConfigurationOrder()
    {
        var first = Version("radarr", null, null, null, 0, probed: false);
        var second = Version("radarr-4k", null, null, null, 1, probed: false);

        Assert.Equal("radarr", VersionSelector.Choose([second, first], DeviceCapabilities.Any, null)!.File.InstanceKey);
    }

    [Fact]
    public void NoVersions_IsNull() => Assert.Null(VersionSelector.Choose([], DeviceCapabilities.Any, null));

    private static VersionCandidate Version(string instance, string? codec, int? height, HdrFormat? hdr, int order, bool probed = true,
        long sizeBytes = 0)
    {
        var file = new MediaFile
        {
            InstanceKey = instance,
            Path = $"/{instance}.mkv",
            ProbeStatus = probed ? ProbeStatus.Probed : ProbeStatus.Pending,
            SizeBytes = sizeBytes,
            DurationMs = probed ? 7_200_000 : null
        };
        var video = codec is null ? null : new MediaStream { Id = instance, Kind = StreamKind.Video, Codec = codec, Height = height, HdrFormat = hdr };
        return new VersionCandidate(file, video, order);
    }
}
