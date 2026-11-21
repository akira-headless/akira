namespace Akira.Server.Events;

public sealed class EventOptions
{
    public const string SectionName = "Events";

    // How long a client can be offline and still catch up event by event. After that it reloads what it shows.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}
