using Sangam.MemberFamily.Application.Abstractions;

namespace Sangam.MemberFamily.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
