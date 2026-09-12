using Sangam.Matrimony.Application.Abstractions;

namespace Sangam.Matrimony.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
