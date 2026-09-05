using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Application.Users.Commands.ChangePassword;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class UserChangePasswordTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static User Register() =>
        User.Register(TenantId, "ravi@example.com", "Ravi Shah", "old-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

    [Fact]
    public void Replaces_the_password_hash()
    {
        var user = Register();

        user.ChangePassword("new-hash", Now);

        user.PasswordHash.Should().Be("new-hash");
    }

    [Fact]
    public void Raises_PasswordChanged_for_the_audit_trail_and_nothing_else()
    {
        var user = Register();
        user.ClearDomainEvents();

        user.ChangePassword("new-hash", Now);

        var raised = user.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<PasswordChangedDomainEvent>().Subject;

        raised.UserId.Should().Be(user.Id);
        raised.TenantId.Should().Be(TenantId);
    }
}

public sealed class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_well_formed_command()
    {
        _validator.Validate(new ChangePasswordCommand("old-password", "a-new-long-password"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_new_password_shorter_than_ten_characters()
    {
        var result = _validator.Validate(new ChangePasswordCommand("old-password", "short"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void Rejects_a_new_password_identical_to_the_current_one()
    {
        var result = _validator.Validate(new ChangePasswordCommand("same-password", "same-password"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void Rejects_an_empty_current_password()
    {
        var result = _validator.Validate(new ChangePasswordCommand("", "a-new-long-password"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword));
    }
}

public sealed class ChangePasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IFailedLoginRecorder _failedLoginRecorder = Substitute.For<IFailedLoginRecorder>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ChangePasswordCommandHandler _handler;
    private readonly User _user;

    public ChangePasswordCommandHandlerTests()
    {
        _user = User.Register(TenantId, "ravi@example.com", "Ravi Shah", "old-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(_user.Id);
        _hasher.Verify("correct-horse", "old-hash").Returns(true);
        _hasher.Hash("a-new-long-password").Returns("new-hash");

        // GetSelfAsync, not GetByIdAsync - the same reason StepUpAuthentication
        // reads past the tenant filter: a Super Admin's own account lives at
        // User.PlatformTenantId, outside whatever Samaaj they act on.
        _users.GetSelfAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

        _handler = new ChangePasswordCommandHandler(
            _users, _hasher, _failedLoginRecorder, _sessions, _unitOfWork, _currentUser, _clock);
    }

    private Task<Result<ChangePasswordResponse>> Handle(string currentPassword, string newPassword = "a-new-long-password") =>
        _handler.Handle(new ChangePasswordCommand(currentPassword, newPassword), CancellationToken.None);

    [Fact]
    public async Task Changes_the_password_when_the_current_one_is_right()
    {
        var result = await Handle("correct-horse");

        result.IsSuccess.Should().BeTrue();
        _user.PasswordHash.Should().Be("new-hash");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ends_every_other_session_once_the_password_changes()
    {
        await Handle("correct-horse");

        await _sessions.Received(1).EndAllForUserAsync(
            _user.Id, SessionEndReason.PasswordChanged, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_wrong_current_password_and_leaves_the_account_alone()
    {
        var result = await Handle("guess");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IStepUpAuthentication.StepUpFailedCode);

        // Forbidden, not Unauthorized - a 401 would make the portals' own
        // interceptor renew the token and retry the request, submitting the
        // change a second time.
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        _user.PasswordHash.Should().Be("old-hash");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Counts_a_wrong_current_password_toward_the_same_lockout_as_a_failed_login()
    {
        await Handle("guess");

        await _failedLoginRecorder.Received(1).RecordAsync(_user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_account_that_is_locked_out()
    {
        for (var i = 0; i < User.MaxFailedAttempts; i++)
        {
            _user.RecordFailedLogin(Now);
        }

        var result = await Handle("correct-horse");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.LockedOut");
        _user.PasswordHash.Should().Be("old-hash");
    }

    [Fact]
    public async Task Refuses_an_account_that_is_not_active()
    {
        var pending = User.Invite(TenantId, "new@example.com", "New Admin",
            AuthorizationCatalog.RoleIds.Member, [AuthorizationCatalog.RoleIds.SamaajAdmin],
            Guid.NewGuid(), Now);

        _currentUser.UserId.Returns(pending.Id);
        _users.GetSelfAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await Handle("correct-horse");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.NotActive");
    }

    [Fact]
    public async Task Fails_cleanly_when_the_account_is_already_gone()
    {
        _users.GetSelfAsync(_user.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Handle("correct-horse");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.NotFound");
    }
}
