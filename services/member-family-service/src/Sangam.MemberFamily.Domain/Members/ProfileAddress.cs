namespace Sangam.MemberFamily.Domain.Members;

/// <summary>
/// One address on a profile - the free-text line a member types, plus the
/// City and Locality they picked from master-data-service.
/// </summary>
/// <remarks>
/// One shape, used twice: <see cref="MemberProfile.PermanentAddress"/> is
/// always present (an empty one when nothing is set yet, the same "always
/// present, fields nullable" shape <see cref="FieldPrivacy"/> already uses),
/// and <see cref="MemberProfile.TemporaryAddress"/> is nullable as a whole -
/// "no temporary address" is a real, distinct state from "one with nothing
/// filled in".
///
/// <see cref="CityId"/> and <see cref="LocalityId"/> are master-data-service
/// ids held as plain foreign keys with no referential integrity across the
/// service boundary - the same stance that service's own CLAUDE.md states
/// for every other service holding one of its ids.
/// </remarks>
public sealed record ProfileAddress(string? Line, Guid? CityId, Guid? LocalityId)
{
    /// <summary>Nothing filled in yet. Distinct from a null <c>TemporaryAddress</c>.</summary>
    public static readonly ProfileAddress Empty = new(null, null, null);
}
