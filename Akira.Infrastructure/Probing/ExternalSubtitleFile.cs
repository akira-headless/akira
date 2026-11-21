using Akira.Core.Media;

namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents a subtitle file found next to a video, with what its name says about it.
/// </summary>
public sealed record ExternalSubtitleFile(string FileName, string FullPath, ExternalSubtitleName Name);
