using Sangam.Pathshala.Application.Abstractions;

namespace Sangam.Pathshala.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
