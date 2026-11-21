namespace Akira.Server.Events;

public static class EventTypes
{
    public const string ItemAdded = "library.item.added";
    public const string ItemUpdated = "library.item.updated";
    public const string ItemRemoved = "library.item.removed";
    public const string FileReplaced = "library.file.replaced";
    public const string WatchStateChanged = "watchstate.changed";
    public const string TrackPreferenceChanged = "trackpreference.changed";
    public const string ScanProgress = "scan.progress";
    public const string ActionCompleted = "action.completed";
    public const string SubtitleRequestUpdated = "subtitle.request.updated";
    public const string PlaybackStarted = "playback.started";
    public const string PlaybackStopped = "playback.stopped";

    // What an outgoing webhook may ask for
    public static readonly IReadOnlyList<string> Stored =
    [
        ItemAdded, ItemUpdated, ItemRemoved, FileReplaced, WatchStateChanged, TrackPreferenceChanged, ScanProgress, ActionCompleted,
        SubtitleRequestUpdated, PlaybackStarted, PlaybackStopped
    ];

    // Only sent on the WebSocket and never stored, so they never show up in a catch-up
    public const string StreamReady = "stream.ready";
    public const string StreamResync = "stream.resync";

    // Sent only to the device it is for, while it is connected
    public const string RemoteCommand = "remote.command";

    // Sent to the user's open connections while a session plays, at most every five seconds, and never stored
    public const string PlaybackProgress = "playback.progress";
}
