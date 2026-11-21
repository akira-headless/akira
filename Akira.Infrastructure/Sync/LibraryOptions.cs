namespace Akira.Infrastructure.Sync;

public sealed class LibraryOptions
{
    public const string SectionName = "Library";
    public static readonly TimeSpan MinimumSyncInterval = TimeSpan.FromMinutes(1);

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(30);
}
