using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Checkout.Infrastructure.Persistence;

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    toDb => toDb.Kind == DateTimeKind.Local ? toDb.ToUniversalTime() : toDb,
    fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));
