using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Akira.Core.Entities;

namespace Akira.Infrastructure.Data.Configurations;

public class OutgoingWebhookConfiguration : IEntityTypeConfiguration<OutgoingWebhook>
{
    public void Configure(EntityTypeBuilder<OutgoingWebhook> builder)
    {
        builder.ToTable("outgoing_webhooks");

        builder.Property(webhook => webhook.Name).HasMaxLength(100);
        builder.Property(webhook => webhook.Url).HasColumnType("text");
        builder.Property(webhook => webhook.EventTypes).HasMaxLength(1000);
        builder.Property(webhook => webhook.ProtectedSecret).HasColumnType("text");
        builder.Property(webhook => webhook.LastError).HasColumnType("text");
    }
}
