using Sangam.SocialIssues.Application.Abstractions;

namespace Sangam.SocialIssues.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
