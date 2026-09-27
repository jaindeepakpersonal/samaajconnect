using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.Login;

/// <summary>
/// What both sign-in steps do alike - the password step on a trusted device,
/// and the code step - so the two cannot disagree about who may sign in or what
/// a session is.
/// </summary>
internal static class SignInSteps
{
    /// <summary>One message for "no such account" and for a wrong password - see LoginCommandHandler.</summary>
    public static Error InvalidCredentials() =>
        Error.Unauthorized("Auth.InvalidCredentials", "Incorrect mobile/email or password.");

    public static Error LockedOut() =>
        Error.Forbidden("Auth.LockedOut", "Too many failed attempts. Try again in a few minutes.");

    /// <summary>
    /// The account must be active and, unless it is the platform's own, its
    /// Samaaj too. Returns the Samaaj (null for a platform administrator).
    /// </summary>
    public static async Task<(Error? Refusal, Tenant? Tenant)> CheckAccountAsync(
        User user, ITenantRepository tenants, CancellationToken cancellationToken)
    {
        if (user.Status != UserStatus.Active)
        {
            return (Error.Forbidden("Auth.AccountSuspended", "This account has been suspended."), null);
        }

        // A Super Admin belongs to the platform, not to a Samaaj, so there is no
        // tenant to check the status of.
        if (user.IsPlatformAdministrator)
        {
            return (null, null);
        }

        var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);

        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return (Error.Forbidden(
                "Auth.SamaajUnavailable",
                "Your Samaaj is not currently active. Please contact your Samaaj administrator."), null);
        }

        return (null, tenant);
    }

    /// <summary>
    /// Records the login, begins the session and mints the access token. Begun
    /// before the save, so the session row and the recorded login commit
    /// together - a login that succeeded without leaving a session would hand
    /// out an access token nothing could ever renew.
    /// </summary>
    public static async Task<LoginResponse> BeginSessionAsync(
        User user,
        Tenant? tenant,
        IUserRepository users,
        ITokenIssuer tokenIssuer,
        ISessionService sessions,
        IUnitOfWork unitOfWork,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var authorization = await users.GetAuthorizationAsync(user.Id, user.TenantId, cancellationToken);

        user.RecordSuccessfulLogin(now);

        var session = await sessions.BeginAsync(user.Id, user.TenantId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokenIssuer.Issue(
            user.Id, user.TenantId, user.MobileOrEmail, authorization.Roles, authorization.Permissions,
            authorization.AreaLocalityId);

        return new LoginResponse(
            token.Token,
            token.ExpiresAt,
            session.RefreshToken,
            session.ExpiresAt,
            user.Id,
            user.TenantId,
            tenant?.Slug ?? string.Empty,
            user.FullName,
            authorization.Roles);
    }

    /// <summary>
    /// Where the code went, recognisable to its owner and to nobody else:
    /// "ra****@example.com", "******3210". The member typed the identifier a
    /// moment ago; this confirms which address, without echoing it whole.
    /// </summary>
    public static string MaskContact(string mobileOrEmail)
    {
        var at = mobileOrEmail.IndexOf('@');

        if (at > 0)
        {
            var local = mobileOrEmail[..at];
            var shown = local.Length <= 2 ? local[..1] : local[..2];

            return shown + new string('*', Math.Max(local.Length - shown.Length, 3)) + mobileOrEmail[at..];
        }

        var digits = mobileOrEmail.Length;

        return digits <= 4
            ? new string('*', digits)
            : new string('*', digits - 4) + mobileOrEmail[^4..];
    }
}
