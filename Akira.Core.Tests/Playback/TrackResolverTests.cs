using Akira.Core.Enum;
using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class TrackResolverTests
{
    // A typical fansub or BD anime release: Japanese FLAC first and default, an English dub,
    // a Signs & Songs track for the dub and the full English subtitles, both unflagged
    private static Tracks Anime() => new Tracks()
        .Audio("jpn", "jpn", "flac", 2, "Japanese", isDefault: true)
        .Audio("eng", "eng", "aac", 2, "English")
        .Subtitle("signs", "eng", "ass", events: 41, title: "Signs & Songs")
        .Subtitle("full", "eng", "ass", events: 412, title: "Full Subtitles", isDefault: true);

    // A Blu-ray remux: TrueHD Atmos with an AC3 compatibility track, a commentary, dubs, and a stack of PGS
    private static Tracks Remux() => new Tracks()
        .Audio("truehd", "eng", "truehd", 8, "TrueHD Atmos 7.1", isDefault: true)
        .Audio("ac3", "eng", "ac3", 6, "AC3 5.1")
        .Audio("commentary", "eng", "ac3", 2, "Director's Commentary", commentary: true)
        .Audio("fre", "fre", "ac3", 6, "French")
        .Audio("ger", "ger", "dts", 6, "German", profile: "DTS-HD MA")
        .Subtitle("eng-forced", "eng", "hdmv_pgs_subtitle", events: 30, forced: true)
        .Subtitle("eng", "eng", "hdmv_pgs_subtitle", events: 1800)
        .Subtitle("eng-sdh", "eng", "hdmv_pgs_subtitle", events: 2100, hearingImpaired: true)
        .Subtitle("fre", "fre", "hdmv_pgs_subtitle", events: 1700)
        .Subtitle("ger", "ger", "hdmv_pgs_subtitle", events: 1750)
        .Subtitle("dut", "dut", "hdmv_pgs_subtitle", events: 1690);

    private static readonly DeviceCapabilities ShieldWithAtmosReceiver = new(
        AudioPassthrough: ["ac3", "eac3", "dts", "dtshd", "truehd"],
        AudioDecode: ["aac", "ac3", "eac3", "flac", "opus"],
        SubtitleFormats: ["srt", "ass", "pgs"]);

    private static readonly DeviceCapabilities TvSpeakers = new(
        AudioDecode: ["aac", "ac3", "eac3", "flac", "opus"],
        SubtitleFormats: ["srt", "ass", "pgs"]);

    [Fact]
    public void NoRules_PicksTheDefaultAudioAndNoSubtitleForANativeLanguage()
    {
        var selection = Remux().Resolve();

        Assert.Equal(("truehd", "default-track"), (selection.Audio.Stream!.Id, selection.Audio.Reason));

        // The default subtitle mode is forced only, and this remux flags its forced track
        Assert.Equal(("eng-forced", "forced"), (selection.Subtitle.Stream!.Id, selection.Subtitle.Reason));
    }

    [Fact]
    public void Anime_OriginalLanguageAndForeignAudio_GivesJapaneseWithFullEnglish()
    {
        var rules = new TrackRules(AudioLanguages: ["eng"], PreferOriginalLanguage: true,
            SubtitleMode: SubtitleMode.WhenAudioForeign, SubtitleLanguages: ["eng"]);

        var selection = Anime().Resolve(rules, original: "jpn");

        Assert.Equal(("jpn", "original-language"), (selection.Audio.Stream!.Id, selection.Audio.Reason));
        Assert.Equal(("full", "audio-foreign"), (selection.Subtitle.Stream!.Id, selection.Subtitle.Reason));
    }

    [Fact]
    public void Anime_Dub_GetsTheUnflaggedSignsTrackAsForced()
    {
        var rules = new TrackRules(AudioLanguages: ["eng"], SubtitleMode: SubtitleMode.WhenAudioForeign, SubtitleLanguages: ["eng"]);

        var selection = Anime().Resolve(rules, original: "jpn");

        Assert.Equal(("eng", "preferred-language"), (selection.Audio.Stream!.Id, selection.Audio.Reason));
        Assert.Equal(("signs", "forced-heuristic"), (selection.Subtitle.Stream!.Id, selection.Subtitle.Reason));
    }

    [Fact]
    public void Anime_AlwaysEnglish_NeverPicksTheSignsTrack()
    {
        var selection = Anime().Resolve(new TrackRules(SubtitleMode: SubtitleMode.Always, SubtitleLanguages: ["eng"]));

        Assert.Equal("full", selection.Subtitle.Stream!.Id);
    }

    [Fact]
    public void Remux_PassthroughReceiver_GetsTrueHd()
    {
        var selection = Remux().Resolve(new TrackRules(AudioLanguages: ["eng"]), ShieldWithAtmosReceiver);

        Assert.Equal(("truehd", "preferred-language"), (selection.Audio.Stream!.Id, selection.Audio.Reason));
    }

    [Fact]
    public void Remux_TvSpeakers_GetTheAc3TrackItCanDecode()
    {
        var selection = Remux().Resolve(new TrackRules(AudioLanguages: ["eng"]), TvSpeakers);

        Assert.Equal("ac3", selection.Audio.Stream!.Id);
    }

    [Fact]
    public void Remux_CommentaryIsExcludedEvenAsTheOnlyStereoTrack()
    {
        var tracks = new Tracks()
            .Audio("commentary", "eng", "aac", 2, "Commentary", isDefault: true, commentary: true)
            .Audio("main", "eng", "aac", 2, "English");

        Assert.Equal("main", tracks.Resolve().Audio.Stream!.Id);
    }

    [Fact]
    public void DtsHdMa_IsPreferredAsLosslessWhenPassedThrough()
    {
        var tracks = new Tracks()
            .Audio("core", "ger", "dts", 6, "German DTS")
            .Audio("ma", "ger", "dts", 6, "German DTS-HD MA", profile: "DTS-HD MA");

        Assert.Equal("ma", tracks.Resolve(new TrackRules(AudioLanguages: ["ger"]), ShieldWithAtmosReceiver).Audio.Stream!.Id);
        Assert.Equal("core", tracks.Resolve(new TrackRules(AudioLanguages: ["ger"], PreferLossless: false), ShieldWithAtmosReceiver).Audio.Stream!.Id);
    }

    [Fact]
    public void Remux_AlwaysWithHearingImpaired_PicksSdh()
    {
        var rules = new TrackRules(SubtitleMode: SubtitleMode.Always, SubtitleLanguages: ["eng"], PreferHearingImpaired: true);

        Assert.Equal("eng-sdh", Remux().Resolve(rules).Subtitle.Stream!.Id);
        Assert.Equal("eng", Remux().Resolve(rules with { PreferHearingImpaired = false }).Subtitle.Stream!.Id);
    }

    [Fact]
    public void Remux_ClientWithoutPgs_GetsNoSubtitleAndSaysWhy()
    {
        var selection = Remux().Resolve(new TrackRules(SubtitleMode: SubtitleMode.Always, SubtitleLanguages: ["eng"]),
            new DeviceCapabilities(SubtitleFormats: ["srt", "ass"]));

        Assert.Equal((null, "no-supported-subtitle"), (selection.Subtitle.Stream, selection.Subtitle.Reason));
    }

    [Fact]
    public void Remux_DutchViewer_GetsFrenchAudioAndDutchSubtitles()
    {
        var rules = new TrackRules(AudioLanguages: ["fre"], SubtitleMode: SubtitleMode.WhenAudioForeign, SubtitleLanguages: ["dut"]);

        var selection = Remux().Resolve(rules, ShieldWithAtmosReceiver);

        Assert.Equal(("fre", "dut"), (selection.Audio.Stream!.Id, selection.Subtitle.Stream!.Id));
    }

    [Fact]
    public void ForcedTrackInAFormatTheClientCannotShow_IsReportedAsSuch()
    {
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("forced", "eng", "hdmv_pgs_subtitle", events: 30, forced: true)
            .Subtitle("dut", "dut", "subrip", events: 900);

        var selection = tracks.Resolve(capabilities: new DeviceCapabilities(SubtitleFormats: ["srt"]));

        Assert.Equal((null, "forced-subtitle-unsupported"), (selection.Subtitle.Stream, selection.Subtitle.Reason));
    }

    [Fact]
    public void UnflaggedForced_IsFoundByEventCount()
    {
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("full", "eng", "subrip", events: 912)
            .Subtitle("parts", "eng", "subrip", events: 37);

        Assert.Equal(("parts", "forced-heuristic"), (tracks.Resolve().Subtitle.Stream!.Id, tracks.Resolve().Subtitle.Reason));
    }

    [Fact]
    public void AShortFullTrack_IsNotMistakenForForced()
    {
        // 28 Weeks Later as found: an embedded "ENG" of 168 events next to a Bazarr download of 654
        var tracks = new Tracks()
            .Audio("eng", "eng", "aac", 8)
            .Subtitle("embedded", "eng", "subrip", events: 168, title: "ENG", isDefault: true)
            .Subtitle("sdh", "eng", "subrip", events: 340, title: "ENG SDH", hearingImpaired: true)
            .Subtitle("bazarr", "eng", "subrip", events: 654, external: true);

        Assert.Equal((null, "no-forced-subtitle"), (tracks.Resolve().Subtitle.Stream, tracks.Resolve().Subtitle.Reason));
    }

    [Fact]
    public void TwoFullTracks_AreNotMistakenForForced()
    {
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("full", "eng", "subrip", events: 912)
            .Subtitle("other", "eng", "subrip", events: 860);

        Assert.Equal((null, "no-forced-subtitle"), (tracks.Resolve().Subtitle.Stream, tracks.Resolve().Subtitle.Reason));
    }

    [Fact]
    public void EventCounts_AreOnlyComparedWithinTextOrImage()
    {
        // PGS counts display sets, roughly two per line, so a full PGS track next to a full SRT is not forced
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("srt", "eng", "subrip", events: 900)
            .Subtitle("pgs", "eng", "hdmv_pgs_subtitle", events: 1800);

        Assert.Null(TrackResolver.ForcedByEventCount(tracks.All.Where(s => s.Kind == StreamKind.Subtitle).ToList()));
    }

    [Fact]
    public void HearingImpaired_IsNeverTakenForForced()
    {
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("sdh", "eng", "subrip", events: 300, hearingImpaired: true)
            .Subtitle("full", "eng", "subrip", events: 900);

        Assert.Null(tracks.Resolve().Subtitle.Stream);
    }

    [Fact]
    public void OnlyAudioTrackUntagged_IsTakenAsTheOriginalLanguage()
    {
        // How SubsPlease and most MP4 releases ship: one "und" AAC track, subtitles next to it
        var tracks = new Tracks()
            .Audio("und", null, "aac", 2)
            .Subtitle("eng", "eng", "ass", events: 300, external: true)
            .Subtitle("dut", "dut", "ass", events: 300, external: true);

        var selection = tracks.Resolve(new TrackRules(SubtitleMode: SubtitleMode.WhenAudioForeign, SubtitleLanguages: ["dut", "eng"]),
            original: "jpn");

        Assert.Equal(("dut", "audio-foreign"), (selection.Subtitle.Stream!.Id, selection.Subtitle.Reason));
    }

    [Fact]
    public void UnknownAudioLanguage_GetsNoForeignSubtitles()
    {
        var tracks = new Tracks()
            .Audio("und", null, "aac", 2)
            .Audio("und2", null, "aac", 2)
            .Subtitle("eng", "eng", "subrip", events: 500);

        var selection = tracks.Resolve(new TrackRules(SubtitleMode: SubtitleMode.WhenAudioForeign, SubtitleLanguages: ["eng"]), original: "jpn");

        Assert.Equal((null, "audio-language-unknown"), (selection.Subtitle.Stream, selection.Subtitle.Reason));
    }

    [Fact]
    public void ForeignFilm_WithoutSubtitlesInTheUsersLanguage_SaysSo()
    {
        var tracks = new Tracks()
            .Audio("kor", "kor", "aac", 2)
            .Subtitle("kor", "kor", "subrip", events: 800);

        var selection = tracks.Resolve(new TrackRules(SubtitleMode: SubtitleMode.WhenAudioForeign, SubtitleLanguages: ["eng"]), original: "kor");

        Assert.Equal((null, "no-matching-subtitle"), (selection.Subtitle.Stream, selection.Subtitle.Reason));
    }

    [Fact]
    public void ExternalSubtitle_FromLingarr_IsPickedLikeAnyOther()
    {
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("eng", "eng", "subrip", events: 900)
            .Subtitle("dut", "dut", "subrip", events: 900, external: true);

        var selection = tracks.Resolve(new TrackRules(SubtitleMode: SubtitleMode.Always, SubtitleLanguages: ["dut"]));

        Assert.Equal("dut", selection.Subtitle.Stream!.Id);
    }

    [Fact]
    public void ForcedFileWithoutLanguage_BelongsToTheAudioLanguage()
    {
        var tracks = new Tracks()
            .Audio("eng", "eng", "eac3", 6)
            .Subtitle("forced", null, "subrip", events: 12, forced: true, external: true);

        Assert.Equal("forced", tracks.Resolve().Subtitle.Stream!.Id);
    }

    [Fact]
    public void ModeOff_ShowsNothing()
    {
        Assert.Equal((null, "mode-off"), (Remux().Resolve(new TrackRules(SubtitleMode: SubtitleMode.Off)).Subtitle.Stream,
            Remux().Resolve(new TrackRules(SubtitleMode: SubtitleMode.Off)).Subtitle.Reason));
    }

    [Fact]
    public void RememberedSeriesChoice_WinsOverRules()
    {
        var anime = Anime();
        var series = new TrackRules(RememberedAudio: TrackSignatures.For(anime["eng"]), RememberedSubtitle: TrackSignatures.For(anime["signs"]));

        var selection = anime.Resolve(new TrackRules(PreferOriginalLanguage: true), original: "jpn", series: series);

        Assert.Equal(("eng", "remembered-series"), (selection.Audio.Stream!.Id, selection.Audio.Reason));
        Assert.Equal(("signs", "remembered-series"), (selection.Subtitle.Stream!.Id, selection.Subtitle.Reason));
    }

    [Fact]
    public void RememberedChoice_SurvivesAnUpgradeThatChangedTheCodec()
    {
        // Remembered on a release with Japanese FLAC, played again on one with Japanese AAC
        var before = Anime();
        var series = new TrackRules(RememberedAudio: TrackSignatures.For(before["jpn"]));
        var after = new Tracks()
            .Audio("eng", "eng", "aac", 2, "English", isDefault: true)
            .Audio("jpn", "jpn", "aac", 2, "Japanese 2.0");

        var selection = after.Resolve(series: series);

        Assert.Equal(("jpn", "remembered-series-language"), (selection.Audio.Stream!.Id, selection.Audio.Reason));
    }

    [Fact]
    public void RememberedCommentary_IsHonouredDespiteTheExclusion()
    {
        var remux = Remux();
        var item = new TrackRules(RememberedAudio: TrackSignatures.For(remux["commentary"]));

        Assert.Equal(("commentary", "remembered-item"), (remux.Resolve(item: item).Audio.Stream!.Id, remux.Resolve(item: item).Audio.Reason));
    }

    [Fact]
    public void RememberedSubtitlesOff_ShowsNothing_AndTheItemOverridesTheSeries()
    {
        var anime = Anime();
        var series = new TrackRules(RememberedSubtitleOff: true);
        var rules = new TrackRules(SubtitleMode: SubtitleMode.Always, SubtitleLanguages: ["eng"]);

        Assert.Equal("remembered-series-off", anime.Resolve(rules, series: series).Subtitle.Reason);

        var item = new TrackRules(RememberedSubtitle: TrackSignatures.For(anime["full"]));
        Assert.Equal(("full", "remembered-item"), (anime.Resolve(rules, series: series, item: item).Subtitle.Stream!.Id,
            anime.Resolve(rules, series: series, item: item).Subtitle.Reason));
    }

    [Fact]
    public void Rules_CombineFieldByField()
    {
        var effective = EffectiveTrackRules.Combine(
            new TrackRules(SubtitleMode: SubtitleMode.Off),
            new TrackRules(AudioLanguages: ["jpn"]),
            new TrackRules(AudioLanguages: ["eng"], SubtitleLanguages: ["dut"], SubtitleMode: SubtitleMode.Always));

        Assert.Equal(["jpn"], effective.AudioLanguages);
        Assert.Equal(["dut"], effective.SubtitleLanguages);
        Assert.Equal(SubtitleMode.Off, effective.SubtitleMode);
        Assert.True(effective.ExcludeCommentary);
        Assert.True(effective.PreferLossless);
    }

    [Fact]
    public void ServerDefault_FillsWhatTheUserLeftOpen_AndTheUserWins()
    {
        var server = new TrackRules(SubtitleLanguages: ["dut", "eng"], SubtitleMode: SubtitleMode.WhenAudioForeign, AudioLanguages: ["eng"]);

        var household = EffectiveTrackRules.Combine(TrackRules.None, TrackRules.None, TrackRules.None, server);
        var kid = EffectiveTrackRules.Combine(TrackRules.None, TrackRules.None, new TrackRules(SubtitleMode: SubtitleMode.Always), server);

        Assert.Equal(["dut", "eng"], household.SubtitleLanguages);
        Assert.Equal(SubtitleMode.WhenAudioForeign, household.SubtitleMode);
        Assert.Equal(["dut", "eng"], kid.SubtitleLanguages);
        Assert.Equal(["eng"], kid.AudioLanguages);
        Assert.Equal(SubtitleMode.Always, kid.SubtitleMode);
        Assert.True(kid.PreferLossless);
    }

    [Fact]
    public void FileWithoutAudio_SaysSo()
    {
        Assert.Equal((null, "no-audio"), (new Tracks().Resolve().Audio.Stream, new Tracks().Resolve().Audio.Reason));
    }

    [Fact]
    public void UnsupportedAudioEverywhere_StillPicksATrack()
    {
        var tracks = new Tracks().Audio("dts", "eng", "dts", 6);

        Assert.Equal("dts", tracks.Resolve(capabilities: new DeviceCapabilities(AudioDecode: ["aac"])).Audio.Stream!.Id);
    }
}
