namespace Sangam.Matrimony.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
/// <remarks>
/// No new permission key is minted for matrimony's own commands - they are
/// all self-scoped (a member only ever acts on their own biodata and their
/// own proposals, checked against `ICurrentUser` in each handler), the same
/// role-only gating `UpdateProfileCommand` already uses for a purely
/// self-scoped write. Only the read side reuses an existing, already
/// broadly-granted key, mirroring `ListGroupsQuery`/`GetGroupQuery`'s own
/// use of it.
/// </remarks>
public static class PermissionKeys
{
    /// <summary>
    /// Search biodata and read one candidate's. Every member holds it - a
    /// biodata search is part of the member directory in the sense that
    /// matters here: who is in this Samaaj.
    /// </summary>
    public const string MembersRead = "Members.Read";
}
