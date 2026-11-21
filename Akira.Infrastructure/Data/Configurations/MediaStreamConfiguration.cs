using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class MediaStreamConfiguration : IEntityTypeConfiguration<MediaStream>
{
    public void Configure(EntityTypeBuilder<MediaStream> builder)
    {
        builder.ToTable("media_streams");

        builder.Property(stream => stream.Id).HasMaxLength(32).ValueGeneratedNever();
        builder.Property(stream => stream.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(stream => stream.Codec).HasMaxLength(50);
        builder.Property(stream => stream.Profile).HasMaxLength(100);
        builder.Property(stream => stream.Language).HasMaxLength(3);
        builder.Property(stream => stream.Title).HasColumnType("text");
        builder.Property(stream => stream.ChannelLayout).HasMaxLength(50);
        builder.Property(stream => stream.HdrFormat).HasConversion<string>().HasMaxLength(20);
        builder.Property(stream => stream.ExternalPath).HasColumnType("text");

        builder.HasIndex(stream => stream.MediaFileId);
    }
}
