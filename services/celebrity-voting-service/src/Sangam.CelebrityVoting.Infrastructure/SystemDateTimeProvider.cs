using Sangam.CelebrityVoting.Application.Abstractions;

namespace Sangam.CelebrityVoting.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
