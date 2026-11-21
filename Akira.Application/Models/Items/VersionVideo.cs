using Akira.Core.Enum;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the video track of a version.
/// </summary>
public sealed record VersionVideo(string? Codec, int? Width, int? Height, HdrFormat? HdrFormat, string? Profile, int? BitDepth, double? FrameRate);
