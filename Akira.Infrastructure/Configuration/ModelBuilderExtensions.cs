using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Akira.Infrastructure.Configuration;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Stores every DateTimeOffset as a UTC DateTime.
    /// </summary>
    public static ModelBuilder UseUtcTimestamps(this ModelBuilder modelBuilder)
    {
        // SQLite has no DateTimeOffset type and EF Core cannot translate ORDER BY or comparisons
        // on one. Stored as a UTC DateTime the column sorts correctly and the queries stay in SQL.
        var converter = new ValueConverter<DateTimeOffset, DateTime>(
            value => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified),
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

        var nullableConverter = new ValueConverter<DateTimeOffset?, DateTime?>(
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value.UtcDateTime, DateTimeKind.Unspecified)
                : null,
            value => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(converter);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(nullableConverter);
            }
        }

        return modelBuilder;
    }
}
