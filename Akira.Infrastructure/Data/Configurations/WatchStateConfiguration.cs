using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class WatchStateConfiguration : IEntityTypeConfiguration<WatchState>
{
    public void Configure(EntityTypeBuilder<WatchState> builder)
    {
        builder.ToTable("watch_states");

        builder.Property(state => state.IdentityKey).HasMaxLength(100);

        // Every library page joins the user's state by identity key
        builder.HasIndex(state => new { state.UserId, state.IdentityKey }).IsUnique();

        builder.HasOne(state => state.User).WithMany().HasForeignKey(state => state.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
