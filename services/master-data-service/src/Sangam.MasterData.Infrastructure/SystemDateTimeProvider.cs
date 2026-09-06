using Sangam.MasterData.Application.Abstractions;

namespace Sangam.MasterData.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
