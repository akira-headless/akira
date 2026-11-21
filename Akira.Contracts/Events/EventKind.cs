namespace Akira.Contracts.Events;

/// <summary>
/// Defines the changes a plugin hears about. A later version may add kinds, so a plugin skips one it does not know.
/// </summary>
public enum EventKind
{
    ItemAdded,
    ItemUpdated,
    ItemRemoved,

    /// <summary>
    /// A movie or episode got another file, an upgrade or a rename.
    /// </summary>
    FileReplaced,

    /// <summary>
    /// The user's progress or play state of a movie or episode changed.
    /// </summary>
    WatchStateChanged
}
