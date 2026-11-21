using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.Property(device => device.Name).HasMaxLength(100);
        builder.Property(device => device.Client).HasMaxLength(50);
        builder.Property(device => device.ClientVersion).HasMaxLength(50);
        builder.Property(device => device.Platform).HasMaxLength(50);
        builder.Property(device => device.Kind).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(device => device.UserId);

        builder.HasMany(device => device.Tokens)
            .WithOne(token => token.Device)
            .HasForeignKey(token => token.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
