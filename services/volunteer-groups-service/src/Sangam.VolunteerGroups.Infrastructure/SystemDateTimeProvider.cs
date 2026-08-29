using Sangam.VolunteerGroups.Application.Abstractions;

namespace Sangam.VolunteerGroups.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
