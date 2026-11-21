using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class SubtitleRequestConfiguration : IEntityTypeConfiguration<SubtitleRequest>
{
    public void Configure(EntityTypeBuilder<SubtitleRequest> builder)
    {
        builder.ToTable("subtitle_requests");

        builder.Property(request => request.Language).HasMaxLength(3);
        builder.Property(request => request.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.ProviderKey).HasMaxLength(32);
        builder.Property(request => request.SourceReference).HasMaxLength(100);
        builder.Property(request => request.ExternalJobId).HasMaxLength(100);
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.ResultStreamId).HasMaxLength(32);
        builder.Property(request => request.ResultPath).HasColumnType("text");
        builder.Property(request => request.Error).HasColumnType("text");
        builder.Property(request => request.TemporaryPath).HasColumnType("text");

        // The worker picks up what is queued or running, a file's open requests are checked for duplicates
        builder.HasIndex(request => request.Status);
        builder.HasIndex(request => new { request.MediaFileId, request.Language });

        builder.HasOne<User>().WithMany().HasForeignKey(request => request.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(request => request.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaFile>().WithMany().HasForeignKey(request => request.MediaFileId).OnDelete(DeleteBehavior.Cascade);
    }
}
