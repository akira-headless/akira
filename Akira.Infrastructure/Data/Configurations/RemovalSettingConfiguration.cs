using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class RemovalSettingConfiguration : IEntityTypeConfiguration<RemovalSetting>
{
    public void Configure(EntityTypeBuilder<RemovalSetting> builder)
    {
        builder.ToTable("removal_settings");

        builder.Property(setting => setting.ScopeKey).HasMaxLength(50);
        builder.Property(setting => setting.Movie).HasConversion<string>().HasMaxLength(30);
        builder.Property(setting => setting.Episode).HasConversion<string>().HasMaxLength(30);
        builder.Property(setting => setting.Season).HasConversion<string>().HasMaxLength(30);
        builder.Property(setting => setting.Series).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(setting => setting.ScopeKey).IsUnique();

        builder.HasOne(setting => setting.User).WithMany().HasForeignKey(setting => setting.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
