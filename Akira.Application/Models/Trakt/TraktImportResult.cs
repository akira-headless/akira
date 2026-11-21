namespace Akira.Server.Models.Trakt;

/// <summary>
/// Represents how many movies and episodes an import from Trakt marked played.
/// </summary>
public sealed record TraktImportResult(int Movies, int Episodes);
