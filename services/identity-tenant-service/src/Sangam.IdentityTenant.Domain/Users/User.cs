using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A login. Tenant-scoped, so it is the first entity in this service the global
/// query filter actually applies to.
/// </summary>
/// <remarks>
/// <see cref="MobileOrEmail"/> is unique <b>platform-wide</b>, not merely per
/// tenant. The member portal offers a "common login" that routes you to your
/// Samaaj without asking which one, and the wireframe states a member joins
/// only one Samaaj — neither is implementable if one identifier can resolve to
/// two users. This is a deliberate strengthening of DATA-MODEL.md section 2.
/// </remarks>
public sealed class User : AggregateRoot, ITenantScopedEntity
{
    /// <summary>
    /// Consecutive failures before the account locks. Lockout is required on
    /// login by SECURITY-CHECKLIST.md; the window is deliberately short so a
    /// forgetful member is inconvenienced rather than locked out for a day.
    /// </summary>
    /// <summary>
    /// TenantId of an account that belongs to the platform rather than to any
    /// one Samaaj - today, only Super Admins. A sentinel rather than a nullable
    /// column so the tenant query filter keeps working unchanged: a
    /// platform account is simply visible when no Samaaj is resolved.
    /// </summary>
    public static readonly Guid PlatformTenantId = Guid.Empty;

    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Mobile number or email address. Stored normalised (trimmed, lowercased).</summary>
    public string MobileOrEmail { get; private set; } = null!;

    public string FullName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public AuthMethod AuthMethod { get; private set; }
    public UserStatus Status { get; private set; }

    /// <summary>
    /// False until the member completes OTP verification. Registration does not
    /// block on it today because no notification channel exists yet — see the
    /// service CLAUDE.md.
    /// </summary>
    public bool IsContactVerified { get; private set; }

    /// <summary>Set when this account came from an approved adult-child conversion.</summary>
    public Guid? ConvertedFromChildProfileId { get; private set; }

    /// <summary>Present only while the account is waiting to be activated.</summary>
    public ActivationCode? ActivationCode { get; private set; }

    /// <summary>Present only while a requested sign-in code has not yet been used or expired.</summary>
    public LoginOtp? LoginOtp { get; private set; }

    /// <summary>Present only while a requested password reset code has not yet been used or expired.</summary>
    public PasswordResetCode? PasswordResetCode { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedOutUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    private User() { }

    public static User Register(
        Guid tenantId,
        string mobileOrEmail,
        string fullName,
        string passwordHash,
        Guid memberRoleId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobileOrEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MobileOrEmail = NormalizeIdentifier(mobileOrEmail),
            FullName = fullName.Trim(),
            PasswordHash = passwordHash,
            AuthMethod = AuthMethod.Password,
            Status = UserStatus.Active,
            IsContactVerified = false,
            CreatedAt = createdAt,
        };

        user._roles.Add(new UserRole(user.Id, memberRoleId, tenantId, createdAt));

        user.Raise(new UserRegisteredDomainEvent(
            user.Id, tenantId, user.MobileOrEmail, user.FullName, createdAt));

        return user;
    }

    /// <summary>
    /// Creates the platform-level Super Admin. The role grant is unscoped
    /// (null tenant), which is how "all tenants" is represented - there is no
    /// separate super-admin flag anywhere to forget to check.
    /// </summary>
    public static User RegisterPlatformAdmin(
        string mobileOrEmail,
        string fullName,
        string passwordHash,
        Guid superAdminRoleId,
        DateTimeOffset createdAt)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = PlatformTenantId,
            MobileOrEmail = NormalizeIdentifier(mobileOrEmail),
            FullName = fullName.Trim(),
            PasswordHash = passwordHash,
            AuthMethod = AuthMethod.Password,
            Status = UserStatus.Active,
            IsContactVerified = true,
            CreatedAt = createdAt,
        };

        user._roles.Add(new UserRole(user.Id, superAdminRoleId, null, createdAt));

        return user;
    }

    public bool IsPlatformAdministrator => TenantId == PlatformTenantId;

    /// <summary>
    /// Creates the account behind an approved adult-child conversion.
    /// </summary>
    /// <remarks>
    /// No password. The Samaaj admin approved that this person is entitled to an
    /// account; nobody has yet proved they are the one asking for it. That
    /// proof is redeeming an activation code, and until then the account
    /// cannot be signed into.
    /// </remarks>
    public static User CreateFromChildConversion(
        Guid tenantId,
        string mobileOrEmail,
        string fullName,
        Guid childProfileId,
        Guid memberRoleId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobileOrEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MobileOrEmail = NormalizeIdentifier(mobileOrEmail),
            FullName = fullName.Trim(),
            // Not an empty string: a blank hash would verify against nothing,
            // but leaving the column meaningfully empty invites someone to
            // "fix" it later. PendingActivation is what actually gates sign-in.
            PasswordHash = string.Empty,
            AuthMethod = AuthMethod.Password,
            Status = UserStatus.PendingActivation,
            IsContactVerified = false,
            ConvertedFromChildProfileId = childProfileId,
            CreatedAt = createdAt,
        };

        user._roles.Add(new UserRole(user.Id, memberRoleId, tenantId, createdAt));

        return user;
    }

    /// <summary>
    /// Creates an account for someone a Samaaj Admin is inviting into a role.
    /// </summary>
    /// <remarks>
    /// Like <see cref="CreateFromChildConversion"/>, the account starts in
    /// <see cref="UserStatus.PendingActivation"/> with no password: inviting
    /// someone establishes that they are entitled to an account, and nobody has
    /// yet proved they are the person asking for it. Redeeming an activation
    /// code is that proof and sets the first password.
    ///
    /// The invited roles are granted up front rather than after activation.
    /// They cannot be used until the account can be signed into, and granting
    /// them here means the invitation is one decision with one audit trail
    /// rather than a grant that someone has to remember to make later.
    ///
    /// Member is granted alongside them. Everyone with a login is a member of
    /// their Samaaj first; the administrative role is what they do in addition.
    /// </remarks>
    public static User Invite(
        Guid tenantId,
        string mobileOrEmail,
        string fullName,
        Guid memberRoleId,
        IEnumerable<Guid> roleIds,
        Guid invitedBy,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobileOrEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MobileOrEmail = NormalizeIdentifier(mobileOrEmail),
            FullName = fullName.Trim(),
            PasswordHash = string.Empty,
            AuthMethod = AuthMethod.Password,
            Status = UserStatus.PendingActivation,
            IsContactVerified = false,
            CreatedAt = createdAt,
        };

        user._roles.Add(new UserRole(user.Id, memberRoleId, tenantId, createdAt));

        foreach (var roleId in roleIds.Distinct().Where(id => id != memberRoleId))
        {
            user._roles.Add(new UserRole(user.Id, roleId, tenantId, createdAt));
        }

        user.Raise(new AdminInvitedDomainEvent(
            user.Id, tenantId, [.. user._roles.Select(r => r.RoleId)], invitedBy, createdAt));

        return user;
    }

    /// <summary>
    /// Attaches a freshly issued activation code, replacing any earlier one.
    /// Re-issuing is normal: codes expire, and paper gets lost.
    /// </summary>
    public void AttachActivationCode(ActivationCode code)
    {
        ActivationCode = code;
    }

    /// <summary>
    /// Completes activation: sets the first password and opens the account.
    /// </summary>
    public void Activate(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        Status = UserStatus.Active;

        // Redeeming a code proves the person holds the contact detail an admin
        // wrote down for them, which is the same assurance OTP would give.
        IsContactVerified = true;

        // Spent, and kept spent: a code that still worked after activation
        // would be a second way into the account.
        ActivationCode = null;

        Raise(new UserActivatedFromChildDomainEvent(
            Id, TenantId, ConvertedFromChildProfileId ?? Guid.Empty, MobileOrEmail, now));
    }

    /// <summary>
    /// Replaces the password on an already-active account, at the member's own
    /// request.
    /// </summary>
    /// <remarks>
    /// The only other place <see cref="PasswordHash"/> is ever written after
    /// construction is <see cref="Activate"/>, moving a
    /// <see cref="UserStatus.PendingActivation"/> account to
    /// <see cref="UserStatus.Active"/> for the first time. Whether *this*
    /// account is in a state that may change its password is the handler's
    /// question, not this method's - the same division <see cref="Activate"/>
    /// itself draws, per its own remarks.
    /// </remarks>
    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        PasswordHash = newPasswordHash;

        Raise(new PasswordChangedDomainEvent(Id, TenantId, now));
    }

    /// <summary>
    /// Attaches a freshly issued password reset code, replacing any earlier
    /// one, and announces it - the same shape as
    /// <see cref="RequestLoginOtp"/>, for the same reason: the notification
    /// pipeline is the only route this code has to whoever asked for it.
    /// </summary>
    public void RequestPasswordReset(PasswordResetCode code, string plaintext, DateTimeOffset now)
    {
        PasswordResetCode = code;

        Raise(new PasswordResetRequestedDomainEvent(Id, TenantId, plaintext, MobileOrEmail, now));
    }

    /// <summary>
    /// Redeems a password reset code: sets a new password and spends the code.
    /// </summary>
    /// <remarks>
    /// A distinct event from <see cref="ChangePassword"/>'s, because this is
    /// not the member proving who they are with a password they already
    /// know - it is proof of the weaker kind, holding the contact address a
    /// code was sent to - and the audit trail should be able to tell the two
    /// apart.
    /// </remarks>
    public void ResetPassword(string newPasswordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        PasswordHash = newPasswordHash;
        PasswordResetCode = null;

        Raise(new PasswordResetDomainEvent(Id, TenantId, now));
    }

    /// <summary>
    /// Attaches a freshly issued sign-in code, replacing any earlier one, and
    /// announces it. Requesting a second code while one is already outstanding
    /// is normal - codes expire, and messages get lost - so the newer one
    /// simply wins.
    /// </summary>
    /// <remarks>
    /// Raises the event itself, unlike <see cref="AttachActivationCode"/>: an
    /// activation code is handed to an admin and returned synchronously, so it
    /// never needs the Outbox, but nothing stands between this code and the
    /// member except the notification pipeline - Raise is how the plaintext
    /// gets there at all.
    /// </remarks>
    public void RequestLoginOtp(LoginOtp otp, string plaintext, DateTimeOffset now)
    {
        LoginOtp = otp;

        Raise(new LoginOtpRequestedDomainEvent(Id, TenantId, plaintext, MobileOrEmail, now));
    }

    /// <summary>
    /// Completes a sign-in by code: spends the code and, if this is the first
    /// time the member has proven they hold their own contact address, marks
    /// it verified.
    /// </summary>
    /// <remarks>
    /// Raises nothing itself - the caller still calls
    /// <see cref="RecordSuccessfulLogin"/> right after, which raises
    /// <see cref="UserLoggedInDomainEvent"/> regardless of which credential
    /// got them there.
    /// </remarks>
    public void CompleteOtpSignIn()
    {
        LoginOtp = null;

        if (!IsContactVerified)
        {
            IsContactVerified = true;
        }
    }

    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil is not null && LockedOutUntil > now;

    /// <summary>
    /// Records a failed password attempt and locks the account once the
    /// threshold is crossed. Returns true if this attempt caused the lockout.
    /// </summary>
    public bool RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts < MaxFailedAttempts)
        {
            return false;
        }

        LockedOutUntil = now.Add(LockoutDuration);
        FailedLoginAttempts = 0;

        return true;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
        LastLoginAt = now;

        Raise(new UserLoggedInDomainEvent(Id, TenantId, now));
    }

    /// <summary>
    /// Records that one of this account's sessions was ended before its
    /// natural expiry — <see cref="SessionRevokedDomainEvent"/>.
    /// </summary>
    /// <remarks>
    /// Changes nothing on the account itself; this exists purely to raise the
    /// event through the Outbox rather than let it stop at a log line. That was
    /// the actual gap: <c>SessionEndReason.ReuseDetected</c> is, in its own
    /// documented words, "the closest thing this platform has to an intrusion
    /// signal", and until this method existed it reached only
    /// <c>ILogger.LogWarning</c> — never the append-only audit trail an
    /// administrator can actually search, because
    /// <c>RevokeSessionOutOfBandAsync</c> revokes the token chain directly
    /// against a raw <c>DbContext</c> rather than through a tracked aggregate,
    /// and nothing tracked meant nothing for the Outbox to drain.
    /// </remarks>
    public void RecordSessionRevoked(
        Guid sessionId, SessionEndReason reason, int tokensRevoked, DateTimeOffset now) =>
        Raise(new SessionRevokedDomainEvent(Id, TenantId, sessionId, reason.ToString(), tokensRevoked, now));

    public void MarkContactVerified() => IsContactVerified = true;

    /// <summary>
    /// Suspends the account. Suspension is checked at login rather than by
    /// deleting the row, so the member's history stays intact and the action
    /// stays reversible.
    /// </summary>
    /// <remarks>
    /// Returns false when already suspended, so a caller can report "nothing to
    /// do" without an event being published for a non-change - the same rule
    /// <c>Tenant.ChangeStatus</c> follows. Refusing a second suspension as an
    /// error would also mean the handler has to check current status itself
    /// before calling this, which is exactly the kind of state the aggregate
    /// should be the one holding.
    /// </remarks>
    public bool Suspend(Guid suspendedBy, DateTimeOffset now)
    {
        if (Status == UserStatus.Suspended)
        {
            return false;
        }

        var previous = Status;
        Status = UserStatus.Suspended;

        Raise(new UserStatusChangedDomainEvent(
            Id, TenantId, previous.ToString(), Status.ToString(), suspendedBy, now));

        return true;
    }

    /// <summary>
    /// Restores an account from suspension. The counterpart to
    /// <see cref="Suspend"/>, and the reason that one is reversible at all.
    /// </summary>
    /// <remarks>
    /// Clears any lockout as well as the status: a suspended account gains
    /// nothing from also coming back locked out until fifteen minutes pass, and
    /// an administrator choosing to reinstate someone plainly intends them to
    /// be able to sign in again immediately.
    /// </remarks>
    public bool Reinstate(Guid reinstatedBy, DateTimeOffset now)
    {
        if (Status != UserStatus.Suspended)
        {
            return false;
        }

        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedOutUntil = null;

        Raise(new UserStatusChangedDomainEvent(
            Id, TenantId, "Suspended", Status.ToString(), reinstatedBy, now));

        return true;
    }

    /// <summary>
    /// Erases the person from this account, keeping only the shell needed to
    /// stop other services' references dangling.
    /// </summary>
    /// <remarks>
    /// The identifier is replaced rather than blanked, because it is uniquely
    /// indexed platform-wide: blanking every erased account to the same empty
    /// string would make the second erasure fail on a unique violation. The
    /// replacement is derived from the row id, so it is unique, obviously not
    /// a real address, and cannot be reversed into the original.
    /// </remarks>
    public void Erase(DateTimeOffset now)
    {
        if (Status == UserStatus.Erased)
        {
            return;
        }

        MobileOrEmail = $"erased-{Id:N}@invalid";
        FullName = "Erased member";
        PasswordHash = string.Empty;
        Status = UserStatus.Erased;
        IsContactVerified = false;
        LastLoginAt = null;
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
        ActivationCode = null;

        // Roles go: they say what this person was allowed to do, which is as
        // much about them as their name.
        _roles.Clear();

        Raise(new UserErasedDomainEvent(Id, TenantId, now));
    }

    public void AssignRole(Guid roleId, Guid? tenantScope, DateTimeOffset now)
    {
        if (_roles.Any(r => r.RoleId == roleId && r.TenantScope == tenantScope))
        {
            return;
        }

        _roles.Add(new UserRole(Id, roleId, tenantScope, now));
    }


    /// <summary>
    /// Grants a role, and announces it. Returns false when the user already
    /// holds it at that scope, so a repeated click is a no-op rather than a
    /// second identical grant and a second audit row.
    /// </summary>
    public bool GrantRole(Guid roleId, Guid? tenantScope, Guid grantedBy, DateTimeOffset now)
    {
        if (_roles.Any(r => r.RoleId == roleId && r.TenantScope == tenantScope))
        {
            return false;
        }

        _roles.Add(new UserRole(Id, roleId, tenantScope, now));

        Raise(new UserRoleGrantedDomainEvent(Id, TenantId, roleId, tenantScope, grantedBy, now));

        return true;
    }

    /// <summary>
    /// Removes a role. Returns false when the user did not hold it, which is
    /// the normal outcome of two admins revoking the same grant at once.
    /// </summary>
    public bool RevokeRole(Guid roleId, Guid? tenantScope, Guid revokedBy, DateTimeOffset now)
    {
        var held = _roles.FirstOrDefault(r => r.RoleId == roleId && r.TenantScope == tenantScope);

        if (held is null)
        {
            return false;
        }

        _roles.Remove(held);

        Raise(new UserRoleRevokedDomainEvent(Id, TenantId, roleId, tenantScope, revokedBy, now));

        return true;
    }

    public bool HasRole(Guid roleId) => _roles.Any(r => r.RoleId == roleId);
    /// <summary>
    /// Lowercases and trims so "Ravi@Example.com " and "ravi@example.com" can
    /// never become two accounts, and so login lookups are exact-match.
    /// </summary>
    public static string NormalizeIdentifier(string mobileOrEmail) =>
        mobileOrEmail.Trim().ToLowerInvariant();
}
