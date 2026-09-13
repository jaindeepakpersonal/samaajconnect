using Sangam.Competition.Application.Abstractions;

namespace Sangam.Competition.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
