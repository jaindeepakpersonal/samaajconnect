using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Abstractions;

public interface IGroupRepository
{
    /// <summary>Tenant-filtered, with applications and members loaded.</summary>
    Task<VolunteerGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This Samaaj's groups, by name.</summary>
    Task<IReadOnlyList<VolunteerGroup>> ListAsync(
        GroupStatus? status, CancellationToken cancellationToken = default);

    /// <summary>True when this Samaaj already has a group by that name.</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    void Add(VolunteerGroup group);
}
