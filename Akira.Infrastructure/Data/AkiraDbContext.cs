using Microsoft.EntityFrameworkCore;
using Akira.Core.Entities;
using Akira.Core.Library;
using Akira.Infrastructure.Configuration;

namespace Akira.Infrastructure.Data;

public class AkiraDbContext : DbContext
{
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<ArrLink> ArrLinks => Set<ArrLink>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaImage> MediaImages => Set<MediaImage>();
    public DbSet<MediaStream> MediaStreams => Set<MediaStream>();
    public DbSet<MediaChapter> MediaChapters => Set<MediaChapter>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<PairingRequest> PairingRequests => Set<PairingRequest>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WatchState> WatchStates => Set<WatchState>();
    public DbSet<TrackPreference> TrackPreferences => Set<TrackPreference>();
    public DbSet<PlaybackSession> PlaybackSessions => Set<PlaybackSession>();
    public DbSet<OutboxEvent> Events => Set<OutboxEvent>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RemovalSetting> RemovalSettings => Set<RemovalSetting>();
    public DbSet<SubtitleRequest> SubtitleRequests => Set<SubtitleRequest>();
    public DbSet<TrickplaySet> TrickplaySets => Set<TrickplaySet>();
    public DbSet<OutgoingWebhook> OutgoingWebhooks => Set<OutgoingWebhook>();
    public DbSet<TraktLink> TraktLinks => Set<TraktLink>();
    public DbSet<EventCursor> EventCursors => Set<EventCursor>();
    public DbSet<MarkerScan> MarkerScans => Set<MarkerScan>();

    public AkiraDbContext(DbContextOptions<AkiraDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AkiraDbContext).Assembly);
        modelBuilder.UseUtcTimestamps();

        // BaseEntity sets its own id, the database never generates one
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => typeof(BaseEntity).IsAssignableFrom(type.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(BaseEntity.Id)).ValueGeneratedNever();
        }
    }

    // The overloads every other one ends in, so no way of saving goes without the timestamps and the folded titles
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        // Search compares folded titles and lists sort them, so every writer of a title keeps them in step without knowing;
        // folded, Écran sorts among the E on SQLite too, where text compares byte by byte
        foreach (var entry in ChangeTracker.Entries<MediaItem>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SearchTitle = SearchText.Fold(entry.Entity.Title);
                entry.Entity.SortTitle = SearchText.Fold(entry.Entity.SortTitle);
            }
        }
    }
}
