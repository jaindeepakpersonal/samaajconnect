namespace Sangam.Pathshala.Domain.Pathshalas;

/// <summary>
/// Where a Pathshala meets: the free-text line a Super Admin types, plus the
/// Country, State, City and Locality picked from master-data-service.
/// </summary>
/// <remarks>
/// Replaced a single free-text <c>Address</c> column on 2026-09-12, the same
/// free-text-to-master-data-pick move member-family-service's own
/// <c>ProfileAddress</c> already made for a member's own address. Nullable as
/// a whole, unlike <c>ProfileAddress.PermanentAddress</c>: a Pathshala's
/// record can simply have no address on it yet, where a member profile always
/// has *a* permanent address object even when every field inside it is empty.
///
/// <see cref="CountryId"/>, <see cref="StateId"/>, <see cref="CityId"/> and
/// <see cref="LocalityId"/> are master-data-service ids held as plain foreign
/// keys with no referential integrity across the service boundary - the same
/// stance that service's own <c>CLAUDE.md</c> states for every other service
/// holding one of its rows' ids.
/// </remarks>
public sealed record PathshalaAddress(
    string? Line, Guid? CountryId, Guid? StateId, Guid? CityId, Guid? LocalityId);
