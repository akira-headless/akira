using Akira.Core.Enum;

namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the event sent when a library item was added, updated or removed. SeriesId is the series a
/// series, season or episode belongs to.
/// </summary>
public sealed record LibraryItemPayload(Guid ItemId, MediaItemType Type, string IdentityKey, Guid? ParentId, Guid? SeriesId);
