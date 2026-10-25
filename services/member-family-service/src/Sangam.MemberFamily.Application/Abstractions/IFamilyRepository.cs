using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Application.Abstractions;

public interface IFamilyRepository
{
    Task<Family?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Family?> GetByCodeAsync(string familyCode, CancellationToken cancellationToken = default);

    /// <summary>The family this member belongs to or has asked to join, if any.</summary>
    Task<Family?> GetForMemberAsync(Guid memberProfileId, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string familyCode, CancellationToken cancellationToken = default);

    /// <summary>Bypasses the tenant filter, for consumers.</summary>
    Task<Family?> GetForConsumerAsync(
        Guid memberProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One household by its own id, bypassing the tenant filter - for the
    /// conversion-completion consumer, which knows the child's family and not
    /// yet any member of it. The caller compares the tenant with the event's.
    /// </summary>
    Task<Family?> GetByIdForConsumerAsync(
        Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The households these members actively belong to, with their members -
    /// one query for a whole directory page rather than one per row.
    /// </summary>
    Task<IReadOnlyList<Family>> ListForMembersAsync(
        IReadOnlyCollection<Guid> memberProfileIds, CancellationToken cancellationToken = default);

    void Add(Family family);

}
