using System.Text;
using System.Text.Unicode;

namespace Akira.Core.Subtitles;

public static class SubtitleDecoder
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    private const int WesternEuropean = 1252;
    private const int CentralEuropean = 1250;
    private const int Cyrillic = 1251;

    // The Windows code page subtitles in a language were written in before UTF-8, by ISO 639-2/B code
    private static readonly Dictionary<string, int> CodePages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rus"] = Cyrillic, ["ukr"] = Cyrillic, ["bel"] = Cyrillic, ["bul"] = Cyrillic, ["mac"] = Cyrillic,
        ["pol"] = CentralEuropean, ["cze"] = CentralEuropean, ["slo"] = CentralEuropean, ["hun"] = CentralEuropean,
        ["hrv"] = CentralEuropean, ["slv"] = CentralEuropean, ["rum"] = CentralEuropean, ["alb"] = CentralEuropean,
        ["gre"] = 1253, ["tur"] = 1254, ["aze"] = 1254, ["heb"] = 1255, ["ara"] = 1256, ["per"] = 1256, ["urd"] = 1256,
        ["est"] = 1257, ["lav"] = 1257, ["lit"] = 1257, ["vie"] = 1258, ["tha"] = 874, ["jpn"] = 932, ["kor"] = 949, ["chi"] = 936
    };

    // Written in Latin or in Cyrillic letters, so the text itself has to say which
    private static readonly HashSet<string> EitherScript = new(StringComparer.OrdinalIgnoreCase) { "srp", "bos" };

    /// <summary>
    /// Reads subtitle bytes as text by their byte order mark, as UTF-8 when they are valid UTF-8, otherwise in the Windows
    /// code page of the track's language (ISO 639-2/B), or Windows-1252 when the language has none.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes, string? language = null)
    {
        if (bytes.StartsWith(Utf8Bom))
        {
            return Encoding.UTF8.GetString(bytes[Utf8Bom.Length..]);
        }

        if (bytes.StartsWith(Utf16LeBom))
        {
            return Encoding.Unicode.GetString(bytes[Utf16LeBom.Length..]);
        }

        if (bytes.StartsWith(Utf16BeBom))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[Utf16BeBom.Length..]);
        }

        if (IsUtf8(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return CodePagesEncodingProvider.Instance.GetEncoding(CodePage(bytes, language))!.GetString(bytes);
    }

    private static int CodePage(ReadOnlySpan<byte> bytes, string? language)
    {
        if (language is not null && CodePages.TryGetValue(language, out var codePage))
        {
            return codePage;
        }

        // A file without a language, as most that sit next to a video under the video's name: text in a Latin alphabet is
        // mostly ASCII letters with an accent here and there, Cyrillic is all bytes above them
        var either = language is not null && EitherScript.Contains(language);
        if ((language is null || either) && MostLettersAreHigh(bytes))
        {
            return Cyrillic;
        }

        // Older subtitles are mostly Windows-1252, and it maps every byte, so a file in another code page still shows all
        // its lines with a few wrong letters rather than losing the lines
        return either ? CentralEuropean : WesternEuropean;
    }

    private static bool MostLettersAreHigh(ReadOnlySpan<byte> bytes)
    {
        int high = 0, ascii = 0;
        foreach (var value in bytes)
        {
            if (value >= 0xC0)
            {
                high++;
            }
            else if (char.IsAsciiLetter((char)value))
            {
                ascii++;
            }
        }

        return high > ascii;
    }

    /// <summary>
    /// Checks whether the bytes are UTF-8 as they are, the only encoding ffmpeg reads a subtitle in.
    /// </summary>
    public static bool IsUtf8(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Utf8Bom) || Utf8.IsValid(bytes);
}
