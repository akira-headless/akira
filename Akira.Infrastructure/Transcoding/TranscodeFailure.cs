namespace Akira.Infrastructure.Transcoding;

public enum TranscodeFailure
{
    NotInstalled,
    Busy,
    OutOfRange,
    // ffmpeg itself failed, which on a GPU switches the server to libx264
    Failed,
    // The session was stopped while a request waited on it
    Stopped,
    // Slower than the video plays: the machine, not the encoder, is the problem
    TooSlow
}
