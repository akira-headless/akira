using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("events");

        builder.HasKey(e => e.Sequence);
        builder.Property(e => e.Sequence).ValueGeneratedOnAdd();
        builder.Property(e => e.Type).HasMaxLength(64);
        builder.Property(e => e.Payload).HasColumnType("text");

        // The retention cleanup deletes by age
        builder.HasIndex(e => e.CreatedAt);

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
