using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class TraktLinkConfiguration : IEntityTypeConfiguration<TraktLink>
{
    public void Configure(EntityTypeBuilder<TraktLink> builder)
    {
        builder.ToTable("trakt_links");

        builder.Property(link => link.Username).HasMaxLength(100);
        builder.Property(link => link.ProtectedAccessToken).HasColumnType("text");
        builder.Property(link => link.ProtectedRefreshToken).HasColumnType("text");
        builder.Property(link => link.LastError).HasColumnType("text");

        builder.HasIndex(link => link.UserId).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(link => link.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
