using Sangam.AuditNotification.Application.Abstractions;

namespace Sangam.AuditNotification.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
