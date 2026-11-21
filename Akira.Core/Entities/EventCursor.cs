namespace Akira.Core.Entities;

// How far a server side reader of the event table got, such as the Trakt sync, so a restart continues there
public class EventCursor : BaseEntity
{
    public required string Consumer { get; set; }
    public long LastSequence { get; set; }
}
