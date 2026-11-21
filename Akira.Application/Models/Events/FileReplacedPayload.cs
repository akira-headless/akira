namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the event sent when a sync found an item's file in an instance replaced by a new one.
/// </summary>
public sealed record FileReplacedPayload(Guid ItemId, string Instance, Guid PreviousFileId, Guid FileId);
