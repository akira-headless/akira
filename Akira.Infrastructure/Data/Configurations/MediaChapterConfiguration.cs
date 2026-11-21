using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class MediaChapterConfiguration : IEntityTypeConfiguration<MediaChapter>
{
    public void Configure(EntityTypeBuilder<MediaChapter> builder)
    {
        builder.ToTable("media_chapters");

        builder.Property(chapter => chapter.Title).HasColumnType("text");

        builder.HasIndex(chapter => chapter.MediaFileId);
    }
}
