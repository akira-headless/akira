using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class ArrLinkConfiguration : IEntityTypeConfiguration<ArrLink>
{
    public void Configure(EntityTypeBuilder<ArrLink> builder)
    {
        builder.ToTable("arr_links");

        builder.Property(link => link.InstanceKey).HasMaxLength(32);

        builder.HasIndex(link => new { link.InstanceKey, link.MediaItemId }).IsUnique();

        // The one above leads with the instance, and removing what no instance holds asks per item
        builder.HasIndex(link => link.MediaItemId);
    }
}
