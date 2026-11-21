using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("media_files");

        builder.Property(file => file.InstanceKey).HasMaxLength(32);
        builder.Property(file => file.Path).HasColumnType("text");
        builder.Property(file => file.Container).HasMaxLength(20);
        builder.Property(file => file.ProbeStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(file => file.ProbeError).HasColumnType("text");

        // An instance holds at most one file per movie or episode
        builder.HasIndex(file => new { file.MediaItemId, file.InstanceKey }).IsUnique();

        // The probe worker looks for pending files on every pass
        builder.HasIndex(file => file.ProbeStatus);

        builder.HasMany(file => file.Streams)
            .WithOne(stream => stream.MediaFile)
            .HasForeignKey(stream => stream.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(file => file.Chapters)
            .WithOne(chapter => chapter.MediaFile)
            .HasForeignKey(chapter => chapter.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
