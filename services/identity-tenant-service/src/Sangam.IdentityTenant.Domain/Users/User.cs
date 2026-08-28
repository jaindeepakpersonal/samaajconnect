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

    public void MarkContactVerified() => IsContactVerified = true;

    /// <summary>
    /// Suspends the account. Suspension is checked at login rather than by
    /// deleting the row, so the member's history stays intact and the action
    /// stays reversible.
    /// </summary>
    public void Suspend() => Status = UserStatus.Suspended;

    public void Reinstate()
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
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
    /// Lowercases and trims so "Ravi@Example.com " and "ravi@example.com" can
    /// never become two accounts, and so login lookups are exact-match.
    /// </summary>
    public static string NormalizeIdentifier(string mobileOrEmail) =>
        mobileOrEmail.Trim().ToLowerInvariant();
}
