using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Consents.Commands.WithdrawConsent;

/// <summary>
/// Withdraws consent for one purpose.
/// </summary>
/// <remarks>
/// DPDP section 6(4) requires withdrawing to be as easy as giving. Giving is a
/// tick during registration, so this is one call with no reason field, no
/// confirmation step, and no admin in the way. A required purpose cannot be
/// withdrawn while the account exists - that is what erasure is for.
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
public sealed record WithdrawConsentCommand(string Purpose)
    : ICommand<IReadOnlyList<ConsentStateResponse>>;
