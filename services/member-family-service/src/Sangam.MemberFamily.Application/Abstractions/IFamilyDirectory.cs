using Sangam.MemberFamily.Application.Families.Queries.ListFamilies;

namespace Sangam.MemberFamily.Application.Abstractions;

/// <summary>
/// The read side of households for administrators: a page of families with
/// their active members and minor children already projected, rather than
/// three aggregates loaded to be flattened in a handler.
/// </summary>
public interface IFamilyDirectory
{
    /// <summary>
    /// Newest first. With a locality, only households with an active member
    /// whose permanent address is there. Tenant-filtered like every read path.
    /// </summary>
    Task<IReadOnlyList<FamilyOverviewResponse>> ListAsync(
        Guid? localityId, int skip, int take, DateOnly today, CancellationToken cancellationToken = default);
}
