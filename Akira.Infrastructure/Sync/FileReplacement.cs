namespace Akira.Infrastructure.Sync;

/// <summary>
/// Represents a version deleted and added again in one sync, an upgrade or a rename. FileId is the new file.
/// </summary>
public sealed record FileReplacement(Guid ItemId, string InstanceKey, Guid PreviousFileId, Guid FileId);
