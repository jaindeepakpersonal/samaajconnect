using Sangam.Boli.Application.Abstractions;

namespace Sangam.Boli.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
