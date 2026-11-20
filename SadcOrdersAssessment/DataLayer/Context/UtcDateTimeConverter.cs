using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DataLayer.Context;

/// <summary>
/// datetime2 has no time zone. Every DateTime we store is UTC, so mark values read back as UTC.
/// That way they serialise with a trailing "Z" and clients don't shift them to local time.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
