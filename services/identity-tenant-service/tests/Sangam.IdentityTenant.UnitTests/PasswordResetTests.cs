using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Users.Commands.RedeemPasswordReset;
using Sangam.IdentityTenant.Application.Users.Commands.RequestPasswordReset;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class PasswordResetCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_returns_a_six_digit_plaintext_and_a_hash_of_it()
    {
        var (code, plaintext) = PasswordResetCode.Issue(p => $"hash({p})", Now);

        plaintext.Should().MatchRegex("^[0-9]{6}$");
        code.Hash.Should().Be($"hash({plaintext})");
    }

    [Fact]
    public void Is_usable_until_its_ten_minute_lifetime_passes()
    {
        var (code, _) = PasswordResetCode.Issue(p => p, Now);

        code.IsUsable(Now.AddMinutes(9)).Should().BeTrue();
        code.IsUsable(Now.AddMinutes(10)).Should().BeFalse();
    }
}

public sealed class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_well_formed_identifier()
    {
        _validator.Validate(new RequestPasswordResetCommand("ravi@example.com"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_an_empty_identifier()
    {
        _validator.Validate(new RequestPasswordResetCommand("")).IsValid.Should().BeFalse();
    }
}

public sealed class RedeemPasswordResetCommandValidatorTests
{
    private readonly RedeemPasswordResetCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_well_formed_command()
    {
        _validator.Validate(new RedeemPasswordResetCommand("ravi@example.com", "123456", "a-new-long-password"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_new_password_shorter_than_ten_characters()
    {
        var result = _validator.Validate(
            new RedeemPasswordResetCommand("ravi@example.com", "123456", "short"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RedeemPasswordResetCommand.NewPassword));
    }

    [Fact]
    public void Rejects_an_empty_code()
    {
        _validator.Validate(new RedeemPasswordResetCommand("ravi@example.com", "", "a-new-long-password"))
            .IsValid.Should().BeFalse();
    }
}

public sealed class RequestPasswordResetCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly RequestPasswordResetCommandHandler _handler;

    private readonly Tenant _tenant;
    private readonly User _user;

    public RequestPasswordResetCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hash({call.Arg<string>()})");

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _user = User.Register(_tenant.Id, "ravi@example.com", "Ravi Shah", "stored-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        _users.FindForLoginAsync("ravi@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);

        _handler = new RequestPasswordResetCommandHandler(_users, _tenants, _hasher, _unitOfWork, _clock);
    }

    private Task<Result<RequestPasswordResetResponse>> Request(string identifier = "ravi@example.com") =>
        _handler.Handle(new RequestPasswordResetCommand(identifier), CancellationToken.None);

    [Fact]
    public async Task A_qualifying_account_gets_a_code_attached_and_saved()
    {
        await Request();

        _user.PasswordResetCode.Should().NotBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_identifier_gets_the_identical_success_response_and_no_code()
    {
        _users.FindForLoginAsync("ghost@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var known = await Request();
        var unknown = await Request(identifier: "ghost@example.com");

        known.IsSuccess.Should().BeTrue();
        unknown.IsSuccess.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_suspended_account_gets_the_same_response_and_no_code()
    {
        _user.Suspend(Guid.NewGuid(), Now);

        var result = await Request();

        result.IsSuccess.Should().BeTrue();
        _user.PasswordResetCode.Should().BeNull();
    }
}

public sealed class RedeemPasswordResetCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IFailedLoginRecorder _failedLogins = Substitute.For<IFailedLoginRecorder>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly RedeemPasswordResetCommandHandler _handler;

    private readonly Tenant _tenant;
    private readonly User _user;

    /// <summary>The real generated plaintext - see LoginOtpTests for why.</summary>
    private readonly string _correctCode;

    private readonly string _wrongCode;

    public RedeemPasswordResetCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hash({call.Arg<string>()})");

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _user = User.Register(_tenant.Id, "ravi@example.com", "Ravi Shah", "old-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        var (code, plaintext) = PasswordResetCode.Issue(_hasher.Hash, Now);
        _correctCode = plaintext;
        _wrongCode = plaintext == "999999" ? "000000" : "999999";
        _user.RequestPasswordReset(code, plaintext, Now);

        _users.FindForLoginAsync("ravi@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _hasher.Verify(_correctCode, $"hash({_correctCode})").Returns(true);

        _handler = new RedeemPasswordResetCommandHandler(
            _users, _hasher, _failedLogins, _sessions, _unitOfWork, _clock);
    }

    private Task<Result<RedeemPasswordResetResponse>> Redeem(
        string identifier = "ravi@example.com", string? code = null, string newPassword = "a-new-long-password") =>
        _handler.Handle(
            new RedeemPasswordResetCommand(identifier, code ?? _correctCode, newPassword), CancellationToken.None);

    [Fact]
    public async Task A_correct_code_sets_the_new_password_and_spends_the_code()
    {
        var result = await Redeem();

        result.IsSuccess.Should().BeTrue();
        _user.PasswordHash.Should().Be("hash(a-new-long-password)");
        _user.PasswordResetCode.Should().BeNull();
    }

    [Fact]
    public async Task A_correct_code_ends_every_session_for_the_account()
    {
        await Redeem();

        await _sessions.Received(1).EndAllForUserAsync(
            _user.Id, SessionEndReason.PasswordChanged, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_code_are_indistinguishable()
    {
        _users.FindForLoginAsync("ghost@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var unknown = await Redeem(identifier: "ghost@example.com");
        var wrongCode = await Redeem(code: _wrongCode);

        unknown.Error.Should().Be(wrongCode.Error);
        unknown.Error.Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task A_wrong_code_is_recorded_and_changes_nothing()
    {
        await Redeem(code: _wrongCode);

        await _failedLogins.Received(1).RecordAsync(_user.Id, Arg.Any<CancellationToken>());
        _user.PasswordHash.Should().Be("old-hash");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_expired_code_is_refused_and_not_spent()
    {
        _clock.UtcNow.Returns(Now.AddMinutes(11));

        var result = await Redeem();

        result.Error.Code.Should().Be("Auth.InvalidCredentials");
        _user.PasswordResetCode.Should().NotBeNull();
    }

    [Fact]
    public async Task A_locked_out_account_is_refused_without_checking_the_code()
    {
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            _user.RecordFailedLogin(Now);
        }

        var result = await Redeem();

        result.Error.Code.Should().Be("Auth.LockedOut");
        _hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }
}
