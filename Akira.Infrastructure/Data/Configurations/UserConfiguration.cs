using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.Property(user => user.Name).HasMaxLength(100);
        builder.Property(user => user.NormalizedName).HasMaxLength(100);
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(user => user.PinHash).HasMaxLength(200);

        builder.HasIndex(user => user.NormalizedName).IsUnique();

        builder.HasMany(user => user.Devices)
            .WithOne(device => device.User)
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(user => user.ApiKeys)
            .WithOne(key => key.User)
            .HasForeignKey(key => key.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
