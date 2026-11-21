namespace Akira.Server.Bridges;

public enum SignedKind
{
    File,
    Subtitle,
    Artwork,
    Hls,
    Progressive,

    // Any item's artwork, for a client that writes artwork urls where its token must not go (its own database)
    LibraryArtwork
}
