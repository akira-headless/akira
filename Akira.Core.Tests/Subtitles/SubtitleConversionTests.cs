using System.Text;
using Akira.Core.Subtitles;
using Xunit;

namespace Akira.Core.Tests.Subtitles;

public class SubtitleConversionTests
{
    // Each cue here is one ffmpeg 7.1 drops or mistimes: Windows-1252 text, a missing blank line, "->" with a period and
    // a short fraction, an end before the start
    private const string Srt =
        "1\r\n00:00:01,000 --> 00:00:02,000\r\n<font color=\"#ffff00\">Café crème</font> & <i>naïve</i>\r\n" +
        "2\r\n00:00:03.5 -> 00:00:04,000\r\n{\\an8}Top\\hline\r\n\r\n" +
        "3\r\n00:00:06,000 --> 00:00:05,000\r\nEnd before start\r\n\r\n" +
        "4\r\n00:00:07,000 --> 00:00:08,000\r\n\r\n";

    private const string Ass = """
        [Script Info]
        ScriptType: v4.00+
        PlayResY: 720

        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1
        Style: Sign,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,8,10,10,10,1

        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,{\blur3\bord4}Wait, what?\NNo\hway.
        Dialogue: 1,0:00:01.00,0:00:02.50,Default,,0,0,0,,Wait, what?\NNo\hway.
        Dialogue: 0,0:00:03.00,0:00:04.00,Sign,,0,0,0,,The Shop
        Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,{\pos(640,100)}Up {translator note}here
        Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,{\p1}m 0 0 l 100 0 100 100{\p0}
        Comment: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,Not shown
        Dialogue: 0:00:07.00,0:00:08.00,Default,,0,0,0,,{\i1}Soft\nbreak{\i0}, no layer
        """;

    [Fact]
    public void Srt_KeepsEveryCueOfAMalformedWindows1252File()
    {
        var cues = SubtitleConversion.Read(Encoding.Latin1.GetBytes(Srt), SubtitleFormat.Srt);

        Assert.Equal(
            "WEBVTT\n\n" +
            "00:00:01.000 --> 00:00:02.000\nCafé crème &amp; <i>naïve</i>\n\n" +
            "00:00:03.500 --> 00:00:04.000 line:0\nTop line\n\n" +
            "00:00:05.000 --> 00:00:06.000\nEnd before start\n",
            WebVttWriter.Write(cues));
        Assert.Equal(
            "1\n00:00:01,000 --> 00:00:02,000\nCafé crème & <i>naïve</i>\n\n" +
            "2\n00:00:03,500 --> 00:00:04,000\n{\\an8}Top line\n\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nEnd before start\n\n",
            SrtWriter.Write(cues));
    }

    [Theory]
    [InlineData(1251, "rus", "Привет, мир! Это не конец.")]
    [InlineData(1251, null, "Привет, мир! Это не конец.")]
    [InlineData(1251, "srp", "Добро вече, како сте?")]
    [InlineData(1250, "srp", "Dobro veče, šta želiš?")]
    [InlineData(1250, "pol", "Zażółć gęślą jaźń, proszę")]
    [InlineData(1250, "cze", "Příliš žluťoučký kůň")]
    [InlineData(1253, "gre", "Καλημέρα, τι κάνεις;")]
    [InlineData(1254, "tur", "Işık ılık süt iç, şoför")]
    [InlineData(1252, null, "Café crème, naïve")]
    public void AFileThatIsNotUtf8_IsReadInTheCodePageOfItsLanguage_OrOfItsLetters(int codePage, string? language, string line)
    {
        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(codePage)!.GetBytes($"1\n00:00:01,000 --> 00:00:02,000\n{line}\n");

        var cue = Assert.Single(SubtitleConversion.Read(bytes, SubtitleFormat.Srt, language));

        Assert.Equal(line, cue.Text);
    }

    [Fact]
    public void Srt_KeepsTheTextAroundALessThanSign_AndReadsPastNumbersTooLongToBeOne()
    {
        const string srt =
            "1\n99999999999999999999:00:01,000 --> 00:00:02,000\nNo time has twenty digits of hours\n\n" +
            "2\n00:00:03,000 --> 00:00:04,000\n{\\an99999999999}3 < 5 and 7 > 2, <i>so</i> x<y</font>\n";

        var cue = Assert.Single(SubtitleConversion.Read(Encoding.UTF8.GetBytes(srt), SubtitleFormat.Srt));

        Assert.Equal("3 &lt; 5 and 7 &gt; 2, <i>so</i> x&lt;y", cue.Text);
    }

    [Fact]
    public void Ass_ShowsEachLineOnceWithoutCodes_AndSignsAtTheTop()
    {
        var cues = SubtitleConversion.Read(Encoding.UTF8.GetBytes(Ass), SubtitleFormat.Ass);

        Assert.Equal(
            "WEBVTT\n\n" +
            "00:00:01.000 --> 00:00:02.500\nWait, what?\nNo way.\n\n" +
            "00:00:03.000 --> 00:00:04.000 line:0\nThe Shop\n\n" +
            "00:00:03.000 --> 00:00:04.000 line:0\nUp here\n\n" +
            "00:00:07.000 --> 00:00:08.000\n<i>Soft break</i>, no layer\n",
            WebVttWriter.Write(cues));
    }
}
