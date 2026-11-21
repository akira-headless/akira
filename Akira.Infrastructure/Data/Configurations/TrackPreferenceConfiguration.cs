using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class TrackPreferenceConfiguration : IEntityTypeConfiguration<TrackPreference>
{
    public void Configure(EntityTypeBuilder<TrackPreference> builder)
    {
        builder.ToTable("track_preferences");

        builder.Property(preference => preference.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(preference => preference.ScopeKey).HasMaxLength(100);
        builder.Property(preference => preference.AudioLanguages).HasMaxLength(200);
        builder.Property(preference => preference.SubtitleMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(preference => preference.SubtitleLanguages).HasMaxLength(200);
        builder.Property(preference => preference.RememberedAudio).HasColumnType("text");
        builder.Property(preference => preference.RememberedSubtitle).HasColumnType("text");

        builder.HasIndex(preference => new { preference.UserId, preference.Scope, preference.ScopeKey }).IsUnique();

        builder.HasOne(preference => preference.User).WithMany().HasForeignKey(preference => preference.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
