using Akira.Infrastructure.Transcoding;

namespace Akira.Server.Services.Playback;

/// <summary>
/// Represents the transcode job of an HLS session, with the size of its file and the width and height of its video.
/// </summary>
internal sealed record HlsStream(TranscodeJob Job, long SizeBytes, int? Width, int? Height);
