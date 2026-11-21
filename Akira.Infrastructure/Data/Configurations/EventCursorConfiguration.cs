using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class EventCursorConfiguration : IEntityTypeConfiguration<EventCursor>
{
    public void Configure(EntityTypeBuilder<EventCursor> builder)
    {
        builder.ToTable("event_cursors");

        builder.Property(cursor => cursor.Consumer).HasMaxLength(50);
        builder.HasIndex(cursor => cursor.Consumer).IsUnique();
    }
}
