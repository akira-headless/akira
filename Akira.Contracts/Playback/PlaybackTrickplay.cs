namespace Akira.Contracts.Playback;

/// <summary>
/// Represents the seek thumbnails of a file: one every IntervalMs, Width by Height each, Columns by Rows to a sheet,
/// Count in all. A sheet is read with IPlaybackAccess.TrickplaySheetAsync.
/// </summary>
public sealed record PlaybackTrickplay(int IntervalMs, int Width, int Height, int Columns, int Rows, int Count);
