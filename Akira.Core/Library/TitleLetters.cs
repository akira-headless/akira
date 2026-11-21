using System.Globalization;
using System.Text;

namespace Akira.Core.Library;

/// <summary>
/// Builds the letters of an A to Z list: where each starts and how many titles it has, so a client can jump to one.
/// </summary>
public static class TitleLetters
{
    public const string Other = "#";

    /// <summary>
    /// Returns the letter a title files under: its first character without accents, upper case, or Other for a digit or symbol.
    /// </summary>
    public static string Of(string sortTitle)
    {
        var first = sortTitle.TrimStart().FirstOrDefault();
        if (first == default)
        {
            return Other;
        }

        // Écran files under E, as it sorts next to it on MariaDB
        var plain = first.ToString().Normalize(NormalizationForm.FormD)
            .FirstOrDefault(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        var letter = char.ToUpperInvariant(plain);
        return letter is >= 'A' and <= 'Z' ? letter.ToString() : Other;
    }

    /// <summary>
    /// Lists the letters of titles already in list order, each with the position of its first title and its count.
    /// </summary>
    public static IReadOnlyList<(string Letter, int Offset, int Count)> Index(IReadOnlyList<string> sortTitles)
    {
        var letters = new List<(string Letter, int Offset, int Count)>();
        for (var i = 0; i < sortTitles.Count; i++)
        {
            var letter = Of(sortTitles[i]);
            var known = letters.FindIndex(entry => entry.Letter == letter);
            if (known < 0)
            {
                letters.Add((letter, i, 1));
            }
            else
            {
                letters[known] = letters[known] with { Count = letters[known].Count + 1 };
            }
        }

        return letters;
    }
}
