using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class TrickplayConfiguration : IEntityTypeConfiguration<TrickplaySet>
{
    public void Configure(EntityTypeBuilder<TrickplaySet> builder)
    {
        builder.ToTable("trickplay");

        builder.Property(trickplay => trickplay.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(trickplay => trickplay.Error).HasColumnType("text");

        builder.HasIndex(trickplay => trickplay.MediaFileId).IsUnique();
        builder.HasOne<MediaFile>().WithMany().HasForeignKey(trickplay => trickplay.MediaFileId).OnDelete(DeleteBehavior.Cascade);
    }
}
