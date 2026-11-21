using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");

        builder.Property(entry => entry.UserName).HasMaxLength(100);
        builder.Property(entry => entry.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(entry => entry.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.IdentityKey).HasMaxLength(100);
        builder.Property(entry => entry.Title).HasColumnType("text");
        builder.Property(entry => entry.Details).HasColumnType("text");

        // The audit list is newest first
        builder.HasIndex(entry => entry.CreatedAt);
    }
}
