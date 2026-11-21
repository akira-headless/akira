namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents a subtitle source or translator. Kind is source or translator.
/// </summary>
public sealed record SubtitleProviderModel(string Key, string Name, string Kind, bool IsConfigured);
