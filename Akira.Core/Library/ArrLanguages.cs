namespace Akira.Core.Library;

/// <summary>
/// Maps the language names Radarr and Sonarr report to ISO 639-2/B codes.
/// </summary>
public static class ArrLanguages
{
    // The bibliographic (B) codes, because that is what Matroska writes in its language tags and
    // what ffprobe reports for the streams these get compared against.
    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Afrikaans"] = "afr",
        ["Albanian"] = "alb",
        ["Arabic"] = "ara",
        ["Azerbaijani"] = "aze",
        ["Bengali"] = "ben",
        ["Bosnian"] = "bos",
        ["Bulgarian"] = "bul",
        ["Catalan"] = "cat",
        ["Chinese"] = "chi",
        ["Croatian"] = "hrv",
        ["Czech"] = "cze",
        ["Danish"] = "dan",
        ["Dutch"] = "dut",
        ["English"] = "eng",
        ["Estonian"] = "est",
        ["Finnish"] = "fin",
        ["Flemish"] = "dut",
        ["French"] = "fre",
        ["Georgian"] = "geo",
        ["German"] = "ger",
        ["Greek"] = "gre",
        ["Hebrew"] = "heb",
        ["Hindi"] = "hin",
        ["Hungarian"] = "hun",
        ["Icelandic"] = "ice",
        ["Indonesian"] = "ind",
        ["Italian"] = "ita",
        ["Japanese"] = "jpn",
        ["Kannada"] = "kan",
        ["Kazakh"] = "kaz",
        ["Korean"] = "kor",
        ["Latvian"] = "lav",
        ["Lithuanian"] = "lit",
        ["Macedonian"] = "mac",
        ["Malay"] = "may",
        ["Malayalam"] = "mal",
        ["Marathi"] = "mar",
        ["Mongolian"] = "mon",
        ["Norwegian"] = "nor",
        ["Persian"] = "per",
        ["Polish"] = "pol",
        ["Portuguese"] = "por",
        ["Portuguese (Brazil)"] = "por",
        ["Romanian"] = "rum",
        ["Romansh"] = "roh",
        ["Russian"] = "rus",
        ["Serbian"] = "srp",
        ["Slovak"] = "slo",
        ["Slovenian"] = "slv",
        ["Spanish"] = "spa",
        ["Spanish (Latino)"] = "spa",
        ["Swedish"] = "swe",
        ["Tagalog"] = "tgl",
        ["Tamil"] = "tam",
        ["Telugu"] = "tel",
        ["Thai"] = "tha",
        ["Turkish"] = "tur",
        ["Ukrainian"] = "ukr",
        ["Urdu"] = "urd",
        ["Uzbek"] = "uzb",
        ["Vietnamese"] = "vie"
    };

    /// <summary>
    /// Returns the ISO 639-2/B code, or null for placeholders like Unknown and Original and for unlisted names.
    /// </summary>
    public static string? ToIso6392(string? name)
    {
        if (name is null)
        {
            return null;
        }

        return Codes.TryGetValue(name.Trim(), out var code) ? code : null;
    }

    public static bool IsPlaceholder(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim() is "Unknown" or "Original" or "Any";
}
