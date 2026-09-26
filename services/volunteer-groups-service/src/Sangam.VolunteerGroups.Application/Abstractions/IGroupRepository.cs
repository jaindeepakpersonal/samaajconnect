using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Abstractions;

public interface IGroupRepository
{
    /// <summary>Tenant-filtered, with applications and members loaded.</summary>
    Task<VolunteerGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This Samaaj's groups, by name.</summary>
    Task<IReadOnlyList<VolunteerGroup>> ListAsync(
        GroupStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this Samaaj already has a group by that name. Pass
    /// <paramref name="excludingGroupId"/> when checking a rename, so the group
    /// keeping its own name is not mistaken for a collision.
    /// </summary>
    Task<bool> NameExistsAsync(
        string name, Guid? excludingGroupId = null, CancellationToken cancellationToken = default);

    void Add(VolunteerGroup group);
}
