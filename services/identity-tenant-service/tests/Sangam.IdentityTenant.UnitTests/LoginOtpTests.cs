using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Users.Commands.LoginWithOtp;
using Sangam.IdentityTenant.Application.Users.Commands.RequestLoginOtp;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class LoginOtpTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_returns_a_six_digit_plaintext_and_a_hash_of_it()
    {
        var (code, plaintext) = LoginOtp.Issue(p => $"hash({p})", Now);

        plaintext.Should().MatchRegex("^[0-9]{6}$");
        code.Hash.Should().Be($"hash({plaintext})");
    }

    [Fact]
    public void Is_usable_until_its_ten_minute_lifetime_passes()
    {
        var (code, _) = LoginOtp.Issue(p => p, Now);

        code.IsUsable(Now.AddMinutes(9)).Should().BeTrue();
        code.IsUsable(Now.AddMinutes(10)).Should().BeFalse();
    }
}

public sealed class RequestLoginOtpCommandValidatorTests
{
    private readonly RequestLoginOtpCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_well_formed_identifier()
    {
        _validator.Validate(new RequestLoginOtpCommand("ravi@example.com")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_an_empty_identifier()
    {
        _validator.Validate(new RequestLoginOtpCommand("")).IsValid.Should().BeFalse();
    }
}

public sealed class RequestLoginOtpCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly RequestLoginOtpCommandHandler _handler;

    private readonly Tenant _tenant;
    private readonly User _user;

    public RequestLoginOtpCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hash({call.Arg<string>()})");

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _user = User.Register(_tenant.Id, "ravi@example.com", "Ravi Shah", "stored-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        _users.FindForLoginAsync("ravi@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);

        _handler = new RequestLoginOtpCommandHandler(_users, _tenants, _hasher, _unitOfWork, _clock);
    }

    private Task<Result<RequestLoginOtpResponse>> Request(string identifier = "ravi@example.com") =>
        _handler.Handle(new RequestLoginOtpCommand(identifier), CancellationToken.None);

    [Fact]
    public async Task A_qualifying_account_gets_a_code_attached_and_saved()
    {
        await Request();

        _user.LoginOtp.Should().NotBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_identifier_gets_the_identical_success_response()
    {
        _users.FindForLoginAsync("ghost@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var known = await Request();
        var unknown = await Request(identifier: "ghost@example.com");

        known.IsSuccess.Should().BeTrue();
        unknown.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_identifier_never_gets_a_code_or_a_save()
    {
        _users.FindForLoginAsync("ghost@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        await Request(identifier: "ghost@example.com");

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_suspended_account_gets_the_same_response_and_no_code()
    {
        _user.Suspend(Guid.NewGuid(), Now);

        var result = await Request();

        result.IsSuccess.Should().BeTrue();
        _user.LoginOtp.Should().BeNull();
    }

    [Fact]
    public async Task A_locked_out_account_gets_the_same_response_and_no_code()
    {
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            _user.RecordFailedLogin(Now);
        }

        var result = await Request();

        result.IsSuccess.Should().BeTrue();
        _user.LoginOtp.Should().BeNull();
    }

    [Fact]
    public async Task A_deactivated_Samaajs_member_gets_the_same_response_and_no_code()
    {
        _tenant.ChangeStatus(TenantStatus.Inactive, Now);

        var result = await Request();

        result.IsSuccess.Should().BeTrue();
        _user.LoginOtp.Should().BeNull();
    }
}

public sealed class LoginWithOtpCommandValidatorTests
{
    private readonly LoginWithOtpCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_well_formed_command()
    {
        _validator.Validate(new LoginWithOtpCommand("ravi@example.com", "123456"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_an_empty_code()
    {
        _validator.Validate(new LoginWithOtpCommand("ravi@example.com", ""))
            .IsValid.Should().BeFalse();
    }
}

public sealed class LoginWithOtpCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IFailedLoginRecorder _failedLogins = Substitute.For<IFailedLoginRecorder>();
    private readonly ITokenIssuer _tokenIssuer = Substitute.For<ITokenIssuer>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly LoginWithOtpCommandHandler _handler;

    private readonly Tenant _tenant;
    private readonly User _user;

    /// <summary>
    /// The real plaintext <see cref="LoginOtp.Issue"/> generated - not a
    /// literal, since the generator is a real
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/> call
    /// this test does not control. <see cref="_wrongCode"/> is guaranteed
    /// different from it rather than a fixed literal that could, with
    /// vanishingly small probability, collide.
    /// </summary>
    private readonly string _correctCode;

    private readonly string _wrongCode;

    public LoginWithOtpCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hash({call.Arg<string>()})");

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _user = User.Register(_tenant.Id, "ravi@example.com", "Ravi Shah", "stored-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        var (code, plaintext) = LoginOtp.Issue(_hasher.Hash, Now);
        _correctCode = plaintext;
        _wrongCode = plaintext == "999999" ? "000000" : "999999";
        _user.RequestLoginOtp(code, plaintext, Now);

        _users.FindForLoginAsync("ravi@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);
        _users.GetAuthorizationAsync(_user.Id, _tenant.Id, Arg.Any<CancellationToken>())
            .Returns(new UserAuthorization(["Member"], ["Members.Read"]));
        _hasher.Verify(_correctCode, $"hash({_correctCode})").Returns(true);
        _tokenIssuer
            .Issue(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(new AccessToken("signed-token", Now.AddHours(1)));

        _sessions.Begin(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(
            new IssuedSession(_user.Id, _tenant.Id, Guid.NewGuid(), "refresh-token", Now.AddDays(14)));

        _handler = new LoginWithOtpCommandHandler(
            _users, _tenants, _hasher, _failedLogins, _tokenIssuer, _sessions, _unitOfWork, _clock);
    }

    private Task<Result<Application.Users.LoginResponse>> Login(
        string identifier = "ravi@example.com", string? code = null) =>
        _handler.Handle(new LoginWithOtpCommand(identifier, code ?? _correctCode), CancellationToken.None);

    [Fact]
    public async Task A_correct_code_returns_a_token()
    {
        var result = await Login();

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("signed-token");
    }

    [Fact]
    public async Task A_correct_code_verifies_the_contact_address_and_spends_the_code()
    {
        await Login();

        _user.IsContactVerified.Should().BeTrue();
        _user.LoginOtp.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_code_are_indistinguishable()
    {
        _users.FindForLoginAsync("ghost@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var unknown = await Login(identifier: "ghost@example.com");
        var wrongCode = await Login(code: _wrongCode);

        unknown.Error.Should().Be(wrongCode.Error);
        unknown.Error.Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task A_wrong_code_is_recorded_against_the_same_lockout_a_wrong_password_uses()
    {
        await Login(code: _wrongCode);

        await _failedLogins.Received(1).RecordAsync(_user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_expired_code_is_refused_like_a_wrong_one()
    {
        _clock.UtcNow.Returns(Now.AddMinutes(11));

        var result = await Login();

        result.Error.Code.Should().Be("Auth.InvalidCredentials");
        _user.LoginOtp.Should().NotBeNull("an expired code is refused, not spent");
    }

    [Fact]
    public async Task A_locked_out_account_is_refused_without_checking_the_code()
    {
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            _user.RecordFailedLogin(Now);
        }

        var result = await Login();

        result.Error.Code.Should().Be("Auth.LockedOut");
        _hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_suspended_account_cannot_sign_in_even_with_the_right_code()
    {
        _user.Suspend(Guid.NewGuid(), Now);

        var result = await Login();

        result.Error.Code.Should().Be("Auth.AccountSuspended");
    }

    [Fact]
    public async Task A_member_of_a_deactivated_Samaaj_cannot_sign_in()
    {
        _tenant.ChangeStatus(TenantStatus.Inactive, Now);

        var result = await Login();

        result.Error.Code.Should().Be("Auth.SamaajUnavailable");
    }
}
