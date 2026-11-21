using Akira.Core.Entities;
using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class MarkerTests
{
    private const long Episode = 24 * 60_000;

    [Fact]
    public void AnimeChapters_GiveTheIntroCreditsAndPreview()
    {
        // The chapter names of a typical fansub release
        var markers = Markers.FromChapters(Chapters(("Prologue", 0), ("Opening", 90_000), ("Part A", 180_000), ("Part B", 720_000),
            ("Ending", 1_290_000), ("Preview", 1_380_000)), Episode);

        Assert.Equal([
            new Marker(MarkerKind.Intro, 90_000, 180_000, MarkerSource.Chapters),
            new Marker(MarkerKind.Credits, 1_290_000, 1_380_000, MarkerSource.Chapters),
            new Marker(MarkerKind.Preview, 1_380_000, Episode, MarkerSource.Chapters)
        ], markers);
    }

    [Theory]
    [InlineData("OP", MarkerKind.Intro)]
    [InlineData("op 2", MarkerKind.Intro)]
    [InlineData("Opening Credits", MarkerKind.Intro)]
    [InlineData("Intro", MarkerKind.Intro)]
    [InlineData("ED", MarkerKind.Credits)]
    [InlineData("End Credits", MarkerKind.Credits)]
    [InlineData("Recap", MarkerKind.Recap)]
    [InlineData("Previously on Severance", MarkerKind.Recap)]
    [InlineData("Next Episode Preview", MarkerKind.Preview)]
    [InlineData("Opening Scene", null)]
    [InlineData("Chapter 1", null)]
    [InlineData("Edward arrives", null)]
    [InlineData(null, null)]
    public void ChapterTitles_AreRecognisedOnlyWhenTheyNameTheWholeChapter(string? title, MarkerKind? expected)
    {
        Assert.Equal(expected, Markers.Kind(title));
    }

    [Fact]
    public void AFilmWhoseFirstChapterIsCalledOpening_HasNoIntroToSkip()
    {
        var markers = Markers.FromChapters(Chapters(("Opening", 0), ("The Heist", 1_200_000)), 7_200_000);

        Assert.Empty(markers);
    }

    [Fact]
    public void CreditsInTheFirstHalf_AreNotCredits_AndSplitCreditsBecomeOne()
    {
        var markers = Markers.FromChapters(Chapters(("Credits", 60_000), ("Story", 120_000), ("End Credits", 6_600_000),
            ("Credits", 6_900_000)), 7_200_000);

        Assert.Equal([new Marker(MarkerKind.Credits, 6_600_000, 7_200_000, MarkerSource.Chapters)], markers);
    }

    [Fact]
    public void AScannedRecap_GoesBeforeTheIntro_ButNotPastAnIntroTheChaptersName()
    {
        var scan = new MarkerScan { RecapStartMs = 0, RecapEndMs = 40_000, IntroStartMs = 95_000, IntroEndMs = 130_000 };

        Assert.Equal([
            new Marker(MarkerKind.Recap, 0, 40_000, MarkerSource.Fingerprint),
            new Marker(MarkerKind.Intro, 95_000, 130_000, MarkerSource.Fingerprint)
        ], Markers.WithScan([], scan));

        var fromChapters = Markers.FromChapters(Chapters(("Opening", 30_000), ("Part A", 120_000)), Episode);
        Assert.Equal([MarkerKind.Intro], Markers.WithScan(fromChapters, scan).Select(m => m.Kind));
    }

    [Fact]
    public void TrickplaySheets_PlaceEveryThumbnail_AndTheVttPointsAtThem()
    {
        var layout = new TrickplayLayout(10_000, 320, 180, 10, 10, TrickplayLayout.CountFor(1_015_000, 10_000));

        Assert.Equal((102, 2), (layout.Count, layout.SheetCount));
        Assert.Equal((0, 2880, 1620), layout.Position(99));
        Assert.Equal((1, 320, 0), layout.Position(101));
        Assert.Equal(134, TrickplayLayout.HeightFor(320, 1920, 800));
        var vtt = layout.WebVtt(sheet => $"{sheet}.jpg");
        Assert.StartsWith("WEBVTT\n\n00:00:00.000 --> 00:00:10.000\n0.jpg#xywh=0,0,320,180\n", vtt);
        Assert.EndsWith("00:16:50.000 --> 00:17:00.000\n1.jpg#xywh=320,0,320,180\n", vtt);
    }

    private static List<MediaChapter> Chapters(params (string Title, long StartMs)[] chapters) =>
        chapters.Select((chapter, index) => new MediaChapter { SortOrder = index, Title = chapter.Title, StartMs = chapter.StartMs }).ToList();
}
