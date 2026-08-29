using Sangam.Events.Application.Abstractions;

namespace Sangam.Events.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
