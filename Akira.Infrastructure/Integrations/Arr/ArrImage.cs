namespace Akira.Infrastructure.Integrations.Arr;

/// <summary>
/// Represents an image as Radarr or Sonarr returns it. Url is the instance's own copy, RemoteUrl the image where it
/// came from.
/// </summary>
public sealed record ArrImage(string? CoverType, string? Url, string? RemoteUrl);
