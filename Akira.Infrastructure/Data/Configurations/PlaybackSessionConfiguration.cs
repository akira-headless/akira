using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class PlaybackSessionConfiguration : IEntityTypeConfiguration<PlaybackSession>
{
    public void Configure(EntityTypeBuilder<PlaybackSession> builder)
    {
        builder.ToTable("playback_sessions");

        builder.Property(session => session.IdentityKey).HasMaxLength(100);
        builder.Property(session => session.MemoryKey).HasMaxLength(100);
        builder.Property(session => session.MemoryScope).HasConversion<string>().HasMaxLength(20);
        builder.Property(session => session.AudioStreamId).HasMaxLength(32);
        builder.Property(session => session.SubtitleStreamId).HasMaxLength(32);
        builder.Property(session => session.StreamDecision).HasMaxLength(255);

        builder.HasIndex(session => session.UserId);

        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>().WithMany().HasForeignKey(session => session.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(session => session.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaFile>().WithMany().HasForeignKey(session => session.MediaFileId).OnDelete(DeleteBehavior.Cascade);
    }
}
