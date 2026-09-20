using Sangam.Panch.Application.Abstractions;

namespace Sangam.Panch.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
