using FluentAssertions;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static User Register(string identifier = "Ravi@Example.COM ") =>
        User.Register(TenantId, identifier, " Ravi Shah ", "hash", AuthorizationCatalog.RoleIds.Member, Now);

    [Fact]
    public void Register_normalises_the_identifier_so_casing_cannot_fork_an_account()
    {
        Register().MobileOrEmail.Should().Be("ravi@example.com");
    }

    [Fact]
    public void Register_grants_the_Member_role_scoped_to_the_joining_Samaaj()
    {
        var role = Register().Roles.Should().ContainSingle().Subject;

        role.RoleId.Should().Be(AuthorizationCatalog.RoleIds.Member);
        role.TenantScope.Should().Be(TenantId);
    }

    [Fact]
    public void Register_leaves_the_contact_unverified_until_OTP_lands()
    {
        var user = Register();

        user.Status.Should().Be(UserStatus.Active);
        user.IsContactVerified.Should().BeFalse();
    }

    [Fact]
    public void Register_raises_UserRegistered_for_member_family_and_audit_to_consume()
    {
        var user = Register();

        var raised = user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredDomainEvent>().Subject;

        raised.UserId.Should().Be(user.Id);
        raised.TenantId.Should().Be(TenantId);
        raised.Topic.Should().Be("identity.user.registered.v1");
    }

    [Fact]
    public void A_fresh_account_is_not_locked_out()
    {
        Register().IsLockedOut(Now).Should().BeFalse();
    }

    [Fact]
    public void Failed_attempts_below_the_threshold_do_not_lock_the_account()
    {
        var user = Register();

        for (var attempt = 1; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedLogin(Now).Should().BeFalse();
        }

        user.IsLockedOut(Now).Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(User.MaxFailedAttempts - 1);
    }

    [Fact]
    public void The_threshold_attempt_locks_the_account_for_the_lockout_window()
    {
        var user = Register();

        bool lockedOut = false;

        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            lockedOut = user.RecordFailedLogin(Now);
        }

        lockedOut.Should().BeTrue();
        user.IsLockedOut(Now).Should().BeTrue();
        user.LockedOutUntil.Should().Be(Now.Add(User.LockoutDuration));

        // Counter resets so the next window starts clean rather than locking on
        // the very first attempt after expiry.
        user.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public void The_lockout_expires_on_its_own()
    {
        var user = Register();

        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        user.IsLockedOut(Now.Add(User.LockoutDuration).AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void A_successful_login_clears_the_lockout_and_raises_an_event()
    {
        var user = Register();
        user.RecordFailedLogin(Now);
        user.ClearDomainEvents();

        var loginAt = Now.AddHours(1);
        user.RecordSuccessfulLogin(loginAt);

        user.FailedLoginAttempts.Should().Be(0);
        user.LockedOutUntil.Should().BeNull();
        user.LastLoginAt.Should().Be(loginAt);
        user.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserLoggedInDomainEvent>();
    }

    [Fact]
    public void Assigning_the_same_role_and_scope_twice_is_a_no_op()
    {
        var user = Register();

        user.AssignRole(AuthorizationCatalog.RoleIds.Member, TenantId, Now);

        user.Roles.Should().ContainSingle();
    }

    [Fact]
    public void The_same_role_at_a_different_scope_is_a_separate_grant()
    {
        var user = Register();

        user.AssignRole(AuthorizationCatalog.RoleIds.Member, null, Now);

        user.Roles.Should().HaveCount(2);
    }

    [Fact]
    public void Recording_a_revoked_session_raises_the_event_the_intrusion_signal_needs()
    {
        // Until this existed, SessionEndReason.ReuseDetected - "the closest
        // thing this platform has to an intrusion signal" per its own doc
        // comment - reached only a log line. RevokeSessionOutOfBandAsync
        // revoked the token chain against a raw DbContext with nothing
        // tracked, so there was nothing for the Outbox to drain.
        var user = Register();
        var sessionId = Guid.NewGuid();

        user.ClearDomainEvents();

        user.RecordSessionRevoked(sessionId, SessionEndReason.ReuseDetected, tokensRevoked: 3, Now);

        var raised = user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SessionRevokedDomainEvent>().Subject;

        raised.UserId.Should().Be(user.Id);
        raised.TenantId.Should().Be(TenantId);
        raised.SessionId.Should().Be(sessionId);
        raised.Reason.Should().Be("ReuseDetected");
        raised.TokensRevoked.Should().Be(3);
        raised.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void The_reason_travels_as_the_enum_name_a_reader_can_recognise()
    {
        // Not the numeric value: SessionEndReason's members are declared with
        // explicit integers precisely so a stored payload survives the enum
        // gaining a member in the middle, but a bare "4" in the audit log
        // means nothing without the source open beside it.
        var user = Register();

        user.RecordSessionRevoked(
            Guid.NewGuid(), SessionEndReason.EndedByAdministrator, tokensRevoked: 1, Now);

        user.DomainEvents.OfType<SessionRevokedDomainEvent>()
            .Single().Reason.Should().Be("EndedByAdministrator");
    }

    // ---- Suspending and reinstating ----------------------------------------
    //
    // Suspend() and the old parameterless Reinstate() both worked correctly and
    // were called from nowhere but a unit test's own setup - the domain layer
    // was complete and there was no way in. These tests are against the door,
    // not just the lock: they exercise the versions that raise
    // UserStatusChangedDomainEvent, which is the half that did not exist until
    // there was finally a command to call it from.

    [Fact]
    public void Suspending_changes_the_status_and_raises_the_event()
    {
        var user = Register();
        var admin = Guid.NewGuid();

        var changed = user.Suspend(admin, Now);

        changed.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Suspended);

        var raised = user.DomainEvents.OfType<UserStatusChangedDomainEvent>().Single();
        raised.UserId.Should().Be(user.Id);
        raised.TenantId.Should().Be(TenantId);
        raised.PreviousStatus.Should().Be("Active");
        raised.Status.Should().Be("Suspended");
        raised.ChangedByUserId.Should().Be(admin);
        raised.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void Suspending_an_already_suspended_account_is_a_no_op()
    {
        // The same rule Tenant.ChangeStatus follows: reporting "nothing to do"
        // rather than raising a second event for a repeated click.
        var user = Register();
        user.Suspend(Guid.NewGuid(), Now);
        user.ClearDomainEvents();

        var changed = user.Suspend(Guid.NewGuid(), Now.AddMinutes(1));

        changed.Should().BeFalse();
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reinstating_restores_Active_and_raises_the_event()
    {
        var user = Register();
        user.Suspend(Guid.NewGuid(), Now);
        user.ClearDomainEvents();

        var admin = Guid.NewGuid();
        var changed = user.Reinstate(admin, Now.AddDays(1));

        changed.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);

        var raised = user.DomainEvents.OfType<UserStatusChangedDomainEvent>().Single();
        raised.PreviousStatus.Should().Be("Suspended");
        raised.Status.Should().Be("Active");
        raised.ChangedByUserId.Should().Be(admin);
    }

    [Fact]
    public void Reinstating_also_clears_any_lockout()
    {
        // An administrator choosing to reinstate someone plainly intends them
        // to sign in again immediately, not come back suspended in every way
        // but name for another fifteen minutes.
        var user = Register();

        for (var i = 0; i < User.MaxFailedAttempts; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.LockedOutUntil.Should().NotBeNull();

        user.Suspend(Guid.NewGuid(), Now);
        user.Reinstate(Guid.NewGuid(), Now);

        user.LockedOutUntil.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public void Reinstating_an_account_that_is_not_suspended_is_a_no_op()
    {
        var user = Register();
        user.ClearDomainEvents();

        var changed = user.Reinstate(Guid.NewGuid(), Now);

        changed.Should().BeFalse();
        user.Status.Should().Be(UserStatus.Active);
        user.DomainEvents.Should().BeEmpty();
    }
}
