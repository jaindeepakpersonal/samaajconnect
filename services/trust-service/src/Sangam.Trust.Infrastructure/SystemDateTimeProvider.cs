using Sangam.Trust.Application.Abstractions;

namespace Sangam.Trust.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
