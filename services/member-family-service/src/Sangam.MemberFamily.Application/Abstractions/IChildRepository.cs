using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.Abstractions;

public interface IChildRepository
{
    Task<ChildProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChildProfile>> ListForFamilyAsync(
        Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>Every child in this Samaaj, for the admin's eligibility list.</summary>
    Task<IReadOnlyList<ChildProfile>> ListAllAsync(CancellationToken cancellationToken = default);

    void Add(ChildProfile child);
}

public interface IChildConversionRepository
{
    Task<ChildConversionRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>An undecided request for this child, if one is outstanding.</summary>
    Task<ChildConversionRequest?> GetPendingForChildAsync(
        Guid childProfileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChildConversionRequest>> ListPendingAsync(
        CancellationToken cancellationToken = default);

    void Add(ChildConversionRequest request);
}
