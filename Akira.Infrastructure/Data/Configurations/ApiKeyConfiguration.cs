using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.Property(key => key.Name).HasMaxLength(100);
        builder.Property(key => key.KeyHash).HasMaxLength(64);
        builder.Property(key => key.Prefix).HasMaxLength(20);

        // Every request with an API key looks it up by hash
        builder.HasIndex(key => key.KeyHash).IsUnique();
        builder.HasIndex(key => key.UserId);
    }
}
