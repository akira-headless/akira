using System.Globalization;
using System.Text;

namespace Akira.Core.Library;

/// <summary>
/// Folds text the way search compares it: lower case without accents, so amelie finds Amélie on SQLite too.
/// </summary>
public static class SearchText
{
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToLowerInvariant(c));
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
