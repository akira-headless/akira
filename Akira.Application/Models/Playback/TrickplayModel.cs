namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the trickplay thumbnails of a file. There is one thumbnail every IntervalMs, laid out row
/// by row on sheets of Columns by Rows tiles, Count thumbnails in all.
/// </summary>
public sealed record TrickplayModel(int IntervalMs, int Width, int Height, int Columns, int Rows, int Count, IReadOnlyList<string> Sheets, string VttUrl);
