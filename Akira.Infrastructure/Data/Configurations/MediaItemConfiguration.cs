using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.ToTable("media_items");

        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.IdentityKey).HasMaxLength(100);
        builder.Property(item => item.Title).HasColumnType("text");
        builder.Property(item => item.SortTitle).HasColumnType("text");
        builder.Property(item => item.SearchTitle).HasColumnType("text");
        builder.Property(item => item.Overview).HasColumnType("text");
        builder.Property(item => item.ImdbId).HasMaxLength(MediaItem.ImdbIdLength);
        builder.Property(item => item.OriginalLanguage).HasMaxLength(3);
        builder.Property(item => item.TrailerYoutubeId).HasMaxLength(32);
        builder.Property(item => item.Genres).HasMaxLength(500);
        builder.Property(item => item.Certification).HasMaxLength(MediaItem.CertificationLength);

        builder.HasIndex(item => item.IdentityKey).IsUnique();
        builder.HasIndex(item => item.ParentId);

        // Deleting a series takes its seasons and episodes with it
        builder.HasOne(item => item.Parent)
            .WithMany(item => item.Children)
            .HasForeignKey(item => item.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Links)
            .WithOne(link => link.MediaItem)
            .HasForeignKey(link => link.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Files)
            .WithOne(file => file.MediaItem)
            .HasForeignKey(file => file.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Images)
            .WithOne(image => image.MediaItem)
            .HasForeignKey(image => image.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
