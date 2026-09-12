using Sangam.Businesses.Application.Abstractions;

namespace Sangam.Businesses.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
