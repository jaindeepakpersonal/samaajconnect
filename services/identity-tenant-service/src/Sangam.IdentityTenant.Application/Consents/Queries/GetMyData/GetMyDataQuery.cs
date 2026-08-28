using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Consents.Queries.GetMyData;

/// <summary>
/// Everything identity-tenant-service holds about the caller.
/// </summary>
/// <remarks>
/// DPDP section 11: a Data Principal may obtain a summary of their personal
/// data and of the processing activities undertaken with it. Deliberately
/// per-service - a member's data is spread across identity, member-family and
/// audit, and having one service reach synchronously into the others would
/// undo the service boundaries for a feature used a handful of times a year.
/// Each service exposes its own; see docs/product/DPDP-COMPLIANCE.md.
/// </remarks>
[RequiresRoles(
    Roles.SuperAdmin,
    Roles.SamaajAdmin,
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager)]
public sealed record GetMyDataQuery : IQuery<MyDataResponse>;

/// <summary>
/// <paramref name="ProcessingPurposes"/> is the "processing activities"
/// half of section 11: what the platform does with this data, in the same
/// words the notice used, rather than a list of database columns.
/// </summary>
public sealed record MyDataResponse(
    string ExportedAt,
    string Service,
    MyAccountData Account,
    IReadOnlyList<ConsentRecordResponse> ConsentHistory,
    IReadOnlyList<ConsentStateResponse> CurrentConsents,
    IReadOnlyList<ConsentNoticeItemResponse> ProcessingPurposes,
    IReadOnlyList<string> HeldElsewhere);

public sealed record MyAccountData(
    Guid UserId,
    Guid TenantId,
    string TenantSlug,
    string MobileOrEmail,
    string FullName,
    string Status,
    bool IsContactVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles);
