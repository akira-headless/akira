namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents the body of a request for a subtitle a file does not have yet. Without OptionId the server picks one, and
/// SessionId ties the result to a playback.
/// </summary>
public sealed record CreateSubtitleRequest(string? Language, string? OptionId, Guid? FileId, Guid? SessionId);
