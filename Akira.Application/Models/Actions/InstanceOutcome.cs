using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents what a delete or unmonitor did in one instance. Files and Bytes are what the instance holds for the item,
/// FilesAlreadyGone the files it no longer had when asked to delete them, and Error says why it failed, or on a dry run
/// why it would.
/// </summary>
public sealed record InstanceOutcome(
    string Instance,
    string InstanceName,
    ActionStatus Status,
    int Files,
    long Bytes,
    int FilesDeleted,
    int FilesAlreadyGone,
    int Unmonitored,
    bool Searched,
    string? Error);
