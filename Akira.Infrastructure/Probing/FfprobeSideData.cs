namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents one side data entry of an ffprobe stream, read to recognize Dolby Vision.
/// </summary>
public sealed record FfprobeSideData(string? SideDataType);
