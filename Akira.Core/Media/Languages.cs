using Akira.Core.Library;

namespace Akira.Core.Media;

/// <summary>
/// Normalizes language codes to ISO 639-2/B, the form Matroska writes and the library stores.
/// </summary>
public static class Languages
{
    private static readonly Dictionary<string, string> TwoLetter = new(StringComparer.OrdinalIgnoreCase)
    {
        ["af"] = "afr", ["am"] = "amh", ["ar"] = "ara", ["az"] = "aze", ["be"] = "bel", ["bg"] = "bul",
        ["bn"] = "ben", ["bo"] = "tib", ["bs"] = "bos", ["ca"] = "cat", ["cs"] = "cze", ["cy"] = "wel",
        ["da"] = "dan", ["de"] = "ger", ["el"] = "gre", ["en"] = "eng", ["eo"] = "epo", ["es"] = "spa",
        ["et"] = "est", ["eu"] = "baq", ["fa"] = "per", ["fi"] = "fin", ["fo"] = "fao", ["fr"] = "fre",
        ["ga"] = "gle", ["gl"] = "glg", ["gu"] = "guj", ["he"] = "heb", ["hi"] = "hin", ["hr"] = "hrv",
        ["hu"] = "hun", ["hy"] = "arm", ["id"] = "ind", ["is"] = "ice", ["it"] = "ita", ["ja"] = "jpn",
        ["ka"] = "geo", ["kk"] = "kaz", ["km"] = "khm", ["kn"] = "kan", ["ko"] = "kor", ["ku"] = "kur",
        ["la"] = "lat", ["lb"] = "ltz", ["lo"] = "lao", ["lt"] = "lit", ["lv"] = "lav", ["mk"] = "mac",
        ["ml"] = "mal", ["mn"] = "mon", ["mr"] = "mar", ["ms"] = "may", ["mt"] = "mlt", ["my"] = "bur",
        ["nb"] = "nob", ["ne"] = "nep", ["nl"] = "dut", ["nn"] = "nno", ["no"] = "nor", ["pa"] = "pan",
        ["pl"] = "pol", ["ps"] = "pus", ["pt"] = "por", ["ro"] = "rum", ["ru"] = "rus", ["si"] = "sin",
        ["sk"] = "slo", ["sl"] = "slv", ["so"] = "som", ["sq"] = "alb", ["sr"] = "srp", ["sv"] = "swe",
        ["sw"] = "swa", ["ta"] = "tam", ["te"] = "tel", ["tg"] = "tgk", ["th"] = "tha", ["tl"] = "tgl",
        ["tr"] = "tur", ["uk"] = "ukr", ["ur"] = "urd", ["uz"] = "uzb", ["vi"] = "vie", ["yi"] = "yid",
        ["zh"] = "chi", ["zu"] = "zul"
    };

    // Terminology codes that differ from the bibliographic one. Players and Bazarr write both,
    // for example .nld.srt next to a Matroska track tagged dut.
    private static readonly Dictionary<string, string> Terminology = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bod"] = "tib", ["ces"] = "cze", ["cym"] = "wel", ["deu"] = "ger", ["ell"] = "gre", ["eus"] = "baq",
        ["fas"] = "per", ["fra"] = "fre", ["hye"] = "arm", ["isl"] = "ice", ["kat"] = "geo", ["mkd"] = "mac",
        ["msa"] = "may", ["mya"] = "bur", ["nld"] = "dut", ["ron"] = "rum", ["slk"] = "slo", ["sqi"] = "alb",
        ["zho"] = "chi"
    };

    private static readonly HashSet<string> Undetermined = new(StringComparer.OrdinalIgnoreCase) { "und", "zxx", "mis", "mul" };

    private static readonly HashSet<string> KnownCodes = new(
        TwoLetter.Values.Concat(Terminology.Values).Concat(["fil", "cmn", "yue"]),
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ToTwoLetterCodes =
        TwoLetter.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the ISO 639-1 code of a stored language, for file names and services that speak two letters. Null when it has none.
    /// </summary>
    public static string? ToTwoLetter(string? language) =>
        language is null ? null : ToTwoLetterCodes.GetValueOrDefault(language);

    /// <summary>
    /// Normalizes a language tag from a container. Unknown three letter codes are kept, undetermined ones become null.
    /// </summary>
    public static string? Normalize(string? language)
    {
        var code = BaseCode(language);
        if (code is null || Undetermined.Contains(code))
        {
            return null;
        }

        if (code.Length == 2)
        {
            return TwoLetter.GetValueOrDefault(code);
        }

        if (code.Length == 3 && code.All(char.IsAsciiLetter))
        {
            return Terminology.GetValueOrDefault(code) ?? code.ToLowerInvariant();
        }

        return ArrLanguages.ToIso6392(code);
    }

    /// <summary>
    /// Reads a language from one part of a file name, accepting only codes and names it knows.
    /// </summary>
    public static string? FromToken(string token)
    {
        var code = BaseCode(token);
        if (code is null)
        {
            return null;
        }

        if (code.Length == 2)
        {
            return TwoLetter.GetValueOrDefault(code);
        }

        if (code.Length == 3)
        {
            if (Terminology.GetValueOrDefault(code) is { } bibliographic)
            {
                return bibliographic;
            }

            return KnownCodes.Contains(code) ? code.ToLowerInvariant() : null;
        }

        return ArrLanguages.ToIso6392(code);
    }

    // pt-BR, en_US and zh-Hans keep their language part
    private static string? BaseCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var separator = trimmed.IndexOfAny(['-', '_']);
        if (separator <= 0)
        {
            return trimmed;
        }

        return trimmed[..separator];
    }
}
