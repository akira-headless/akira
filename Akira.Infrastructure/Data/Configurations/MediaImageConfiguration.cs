using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class MediaImageConfiguration : IEntityTypeConfiguration<MediaImage>
{
    public void Configure(EntityTypeBuilder<MediaImage> builder)
    {
        builder.ToTable("media_images");

        builder.Property(image => image.InstanceKey).HasMaxLength(32);
        builder.Property(image => image.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(image => image.ArrFileName).HasMaxLength(MediaImage.ArrFileNameLength);
        builder.Property(image => image.RemoteUrl).HasColumnType("text");
        builder.Property(image => image.BlurHash).HasMaxLength(64);

        // Each instance keeps its own copy, so removing one instance never leaves an item without art
        builder.HasIndex(image => new { image.MediaItemId, image.Kind, image.InstanceKey }).IsUnique();
    }
}
