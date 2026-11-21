using Akira.Core.Enum;
using Akira.Server.Events;
using Akira.Infrastructure.Sync;
using Akira.Server.Models.Events;
using Akira.Server.Services.Events;
using Xunit;

namespace Akira.Server.Tests.Events;

public class LibraryEventsTests
{
    private static readonly ItemChange Heat = Change("movie:tmdb:949");
    private static readonly ItemChange Alien = Change("movie:tmdb:348");
    private static readonly ItemChange Gone = Change("movie:tmdb:1");

    [Fact]
    public void AnItemAddedByOneInstanceAndUpdatedByAnother_IsOneAdded()
    {
        var report = new LibrarySyncReport([Result("radarr", added: [Heat]), Result("radarr-4k", updated: [Heat, Alien])], []);

        var events = LibraryEventService.FromReport(report, ScanKind.Sync);

        Assert.Equal([(EventTypes.ItemAdded, Heat.ItemId), (EventTypes.ItemUpdated, Alien.ItemId)], ItemEvents(events));
    }

    [Fact]
    public void AnItemGoneByTheEnd_IsOnlyRemoved_EvenIfItsFileWasReplacedFirst()
    {
        var replaced = new FileReplacement(Gone.ItemId, "radarr", Guid.NewGuid(), Guid.NewGuid());
        var report = new LibrarySyncReport([Result("radarr", updated: [Gone], unlinked: [Gone], replaced: [replaced])], [Gone]);

        var events = LibraryEventService.FromReport(report, ScanKind.Refresh);

        Assert.Equal([(EventTypes.ItemRemoved, Gone.ItemId)], ItemEvents(events));
        Assert.DoesNotContain(events, e => e.Type == EventTypes.FileReplaced);
    }

    [Fact]
    public void AnUnlinkedItemAnotherInstanceStillHas_IsUpdated()
    {
        var report = new LibrarySyncReport([Result("radarr", unlinked: [Heat])], []);

        Assert.Equal([(EventTypes.ItemUpdated, Heat.ItemId)], ItemEvents(LibraryEventService.FromReport(report, ScanKind.Sync)));
    }

    [Fact]
    public void Summary_ComesLast_WithTheWorstStatus()
    {
        var failed = SyncResult.Failed("sonarr", "Sonarr rejected the API key.");
        var report = new LibrarySyncReport([Result("radarr", added: [Heat]), failed], []);

        var summary = Assert.IsType<ScanProgressPayload>(LibraryEventService.FromReport(report, ScanKind.Sync)[^1].Payload);

        Assert.Equal((ScanKind.Sync, ScanStatus.Failed, 2), (summary.Kind, summary.Status, summary.Instances.Count));
        Assert.Equal(new ScanInstance("radarr", ScanStatus.Succeeded, 1, 0, 0), summary.Instances[0]);
    }

    private static List<(string Type, Guid ItemId)> ItemEvents(IEnumerable<EventDraft> events) =>
        events.Where(e => e.Payload is LibraryItemPayload).Select(e => (e.Type, ((LibraryItemPayload)e.Payload).ItemId)).ToList();

    private static ItemChange Change(string key) => new(Guid.NewGuid(), MediaItemType.Movie, key, null, null);

    private static SyncResult Result(
        string instance,
        ItemChange[]? added = null,
        ItemChange[]? updated = null,
        ItemChange[]? unlinked = null,
        FileReplacement[]? replaced = null) =>
        new(instance, SyncStatus.Succeeded, added ?? [], updated ?? [], unlinked ?? [], replaced ?? [], []);
}
