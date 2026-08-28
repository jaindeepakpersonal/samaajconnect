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

    void Add(Family family);

}
