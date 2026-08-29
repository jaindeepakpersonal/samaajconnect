using Sangam.Timeline.Application.Abstractions;

namespace Sangam.Timeline.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
