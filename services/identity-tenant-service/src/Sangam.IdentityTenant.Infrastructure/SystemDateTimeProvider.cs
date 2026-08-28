using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
