namespace Akira.Infrastructure.Integrations.Lingarr;

/// <summary>
/// Represents a translation request as Lingarr returns it. Progress is a percentage, and TranslatedSubtitle is the path
/// of the result as Lingarr sees it.
/// </summary>
internal sealed record LingarrRequest(string? Status, int Progress, string? TranslatedSubtitle, string? ErrorMessage);
