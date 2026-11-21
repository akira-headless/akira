using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class MarkerScanConfiguration : IEntityTypeConfiguration<MarkerScan>
{
    public void Configure(EntityTypeBuilder<MarkerScan> builder)
    {
        builder.ToTable("marker_scans");

        builder.Property(scan => scan.Error).HasColumnType("text");

        builder.HasIndex(scan => scan.MediaFileId).IsUnique();
        builder.HasOne<MediaFile>().WithMany().HasForeignKey(scan => scan.MediaFileId).OnDelete(DeleteBehavior.Cascade);
    }
}
