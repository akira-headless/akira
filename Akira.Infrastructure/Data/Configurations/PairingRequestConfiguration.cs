using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class PairingRequestConfiguration : IEntityTypeConfiguration<PairingRequest>
{
    public void Configure(EntityTypeBuilder<PairingRequest> builder)
    {
        builder.ToTable("pairing_requests");

        builder.Property(pairing => pairing.Code).HasMaxLength(16);
        builder.Property(pairing => pairing.PollSecretHash).HasMaxLength(64);
        builder.Property(pairing => pairing.DeviceName).HasMaxLength(100);
        builder.Property(pairing => pairing.Client).HasMaxLength(50);
        builder.Property(pairing => pairing.ClientVersion).HasMaxLength(50);
        builder.Property(pairing => pairing.Platform).HasMaxLength(50);
        builder.Property(pairing => pairing.Status).HasConversion<string>().HasMaxLength(20);

        // Approval looks a pending request up by the code the user typed
        builder.HasIndex(pairing => pairing.Code);

        builder.HasOne(pairing => pairing.Device)
            .WithMany()
            .HasForeignKey(pairing => pairing.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
