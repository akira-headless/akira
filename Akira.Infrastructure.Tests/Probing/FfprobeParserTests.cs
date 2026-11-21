using System.Text.Json;
using Akira.Core.Enum;
using Akira.Core.Media;
using Akira.Infrastructure.Probing;
using Xunit;

namespace Akira.Infrastructure.Tests.Probing;

public class FfprobeParserTests
{
    private static readonly Guid FileId = Guid.Parse("01a196ce-263c-7c0d-954d-f08450b4d2da");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    [Fact]
    public void MkvmergeFile_ReadsStatisticsAndTitleFlags()
    {
        var media = Parse("ffprobe-mkvmerge-hevc.json");

        Assert.Equal(5414943, media.DurationMs);
        Assert.Equal([StreamKind.Video, StreamKind.Audio, StreamKind.Subtitle, StreamKind.Subtitle], media.Streams.Select(s => s.Kind));

        var video = media.Streams[0];
        Assert.Equal(("hevc", "Main 10", 10, HdrFormat.Sdr), (video.Codec, video.Profile, video.BitDepth, video.HdrFormat));

        var audio = media.Streams[1];
        Assert.Equal(("aac", "eng", 8, "7.1"), (audio.Codec, audio.Language, audio.Channels, audio.ChannelLayout));

        var (full, sdh) = (media.Streams[2], media.Streams[3]);
        Assert.Equal((168, true, false), (full.EventCount, full.IsTextBased, full.IsHearingImpaired));

        // No hearing impaired disposition, only the title says so
        Assert.Equal((340, true), (sdh.EventCount, sdh.IsHearingImpaired));
        Assert.Equal([0, 1, 2, 3], media.Streams.Select(s => s.IndexHint!.Value));
    }

    [Fact]
    public void Mp4File_ReadsFrameCountsAndTreatsUndAsUnknown()
    {
        var media = Parse("ffprobe-mp4-h264.json");

        Assert.Equal(5047712, media.DurationMs);
        var video = media.Streams[0];
        Assert.Equal(("h264", 8, 1920, HdrFormat.Sdr), (video.Codec, video.BitDepth, video.Width, video.HdrFormat));
        Assert.Equal(23.976, video.FrameRate);
        Assert.Null(media.Streams[1].Language);
        Assert.Equal(6, media.Streams[1].Channels);
    }

    [Fact]
    public void SyntheticFile_ReadsDispositionsHdrAndChapters()
    {
        var media = Parse("ffprobe-synthetic.json");

        Assert.Equal(HdrFormat.Hdr10, media.Streams[0].HdrFormat);

        var audio = media.Streams.Where(s => s.Kind == StreamKind.Audio).ToList();
        Assert.Equal(["jpn", "eng", "eng"], audio.Select(a => a.Language!));
        Assert.Equal([true, false, false], audio.Select(a => a.IsDefault));
        Assert.Equal([false, false, true], audio.Select(a => a.IsCommentary));
        Assert.Equal(("flac", 2), (audio[0].Codec, audio[0].Channels));

        var subtitles = media.Streams.Where(s => s.Kind == StreamKind.Subtitle).ToList();
        Assert.Equal(["Full", "Signs & Songs", "English", "Nederlands"], subtitles.Select(s => s.Title!));
        Assert.Equal([false, false, false, true], subtitles.Select(s => s.IsForced));
        Assert.Equal([false, false, true, false], subtitles.Select(s => s.IsHearingImpaired));
        Assert.All(subtitles, s => Assert.Null(s.EventCount));

        Assert.Equal([("Opening", 0L, 400L), ("Part A", 400L, 1000L)],
            media.Chapters.Select(c => (c.Title!, c.StartMs, c.EndMs!.Value)));
    }

    [Fact]
    public void Ids_AreStableAndUniquePerFile()
    {
        var first = Parse("ffprobe-synthetic.json");
        var second = Parse("ffprobe-synthetic.json");

        Assert.Equal(first.Streams.Select(s => s.Id), second.Streams.Select(s => s.Id));
        Assert.Equal(first.Streams.Count, first.Streams.Select(s => s.Id).Distinct().Count());
        Assert.NotEqual(first.Streams[0].Id, FfprobeParser.Parse(Load("ffprobe-synthetic.json"), Guid.NewGuid()).Streams[0].Id);
    }

    [Fact]
    public void IdenticalTracks_GetDifferentIdsByPosition()
    {
        var output = Load("ffprobe-synthetic.json");
        var english = output.Streams!.Single(s => s.Index == 2);
        var twice = output with { Streams = [english, english with { Index = 9 }] };

        var media = FfprobeParser.Parse(twice, FileId);

        Assert.NotEqual(media.Streams[0].Id, media.Streams[1].Id);
    }

    [Fact]
    public void ExternalSubtitle_TakesLanguageAndFlagsFromItsName()
    {
        var output = Load("ffprobe-external-srt.json");
        var name = SubtitleFiles.Parse("Movie.mkv", "Movie.nld.forced.srt")!;

        var stream = FfprobeParser.ParseExternalSubtitle(output, FileId, "Movie.nld.forced.srt", "/data/Movie.nld.forced.srt", name, 1000)!;

        Assert.Equal(StreamIds.External(FileId, "Movie.nld.forced.srt"), stream.Id);
        Assert.Equal(("dut", true, true, true, 540), (stream.Language, stream.IsForced, stream.IsExternal, stream.IsTextBased, stream.EventCount));
        Assert.Null(stream.IndexHint);
    }

    private static ProbedMedia Parse(string fixture) => FfprobeParser.Parse(Load(fixture), FileId);

    private static FfprobeOutput Load(string fixture) =>
        JsonSerializer.Deserialize<FfprobeOutput>(Fixtures.Read(fixture), SerializerOptions)!;
}
