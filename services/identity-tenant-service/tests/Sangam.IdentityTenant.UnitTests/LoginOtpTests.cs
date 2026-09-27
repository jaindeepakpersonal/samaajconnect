using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Users.Commands.Login;
using Sangam.IdentityTenant.Application.Users.Commands.VerifySignInCode;
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
        var (code, plaintext) = LoginOtp.Issue(p => $"hash({p})", Now, LoginOtp.Lifetime);

        plaintext.Should().MatchRegex("^[0-9]{6}$");
        code.Hash.Should().Be($"hash({plaintext})");
    }

    [Fact]
    public void Is_usable_until_its_ten_minute_lifetime_passes()
    {
        var (code, _) = LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime);

        code.IsUsable(Now.AddMinutes(9)).Should().BeTrue();
        code.IsUsable(Now.AddMinutes(10)).Should().BeFalse();
    }
}

public sealed class SecondFactorTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static User Member() =>
        User.Register(Guid.NewGuid(), "ravi@example.com", "Ravi Shah", "stored-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

    [Fact]
    public void Beginning_holds_the_sign_in_and_sends_the_code()
    {
        var user = Member();
        user.ClearDomainEvents();
        var (challenge, _) = LoginChallenge.Issue(p => $"h({p})", Now, LoginOtp.Lifetime);
        var (otp, code) = LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime);

        user.BeginSecondFactor(challenge, otp, code, Now);

        user.LoginChallenge.Should().BeSameAs(challenge);
        user.LoginOtp.Should().BeSameAs(otp);
        user.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<LoginOtpRequestedDomainEvent>()
            .Which.Code.Should().Be(code);
    }

    [Fact]
    public void A_resend_waits_thirty_seconds_and_never_outlives_the_challenge()
    {
        var user = Member();
        var (challenge, _) = LoginChallenge.Issue(p => p, Now, TimeSpan.FromMinutes(10));
        user.BeginSecondFactor(challenge, LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime).Code, "111111", Now);

        user.ResendSecondFactor(LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime).Code, "222222", Now.AddSeconds(10))
            .Should().BeFalse("a button pressed twice is not two messages");
        user.ResendSecondFactor(LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime).Code, "333333", Now.AddSeconds(31))
            .Should().BeTrue();
        user.ResendSecondFactor(LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime).Code, "444444", Now.AddMinutes(11))
            .Should().BeFalse("a resend does not extend the challenge");
    }

    [Fact]
    public void Completing_spends_both_and_verifies_the_contact_address()
    {
        var user = Member();
        user.BeginSecondFactor(
            LoginChallenge.Issue(p => p, Now, LoginOtp.Lifetime).Challenge,
            LoginOtp.Issue(p => p, Now, LoginOtp.Lifetime).Code, "123456", Now);

        user.CompleteSecondFactor();

        user.LoginChallenge.Should().BeNull();
        user.LoginOtp.Should().BeNull();
        user.IsContactVerified.Should().BeTrue();
    }

    [Fact]
    public void A_trusted_device_lasts_thirty_days_and_can_be_revoked()
    {
        var (device, plaintext) = TrustedDevice.Issue(Guid.NewGuid(), p => $"h({p})", Now);

        plaintext.Length.Should().BeGreaterThanOrEqualTo(40, "256 random bits");
        device.TokenHash.Should().Be($"h({plaintext})");
        device.IsUsable(Now.AddDays(29)).Should().BeTrue();
        device.IsUsable(Now.AddDays(30)).Should().BeFalse();

        device.Revoke(Now.AddDays(1));
        device.IsUsable(Now.AddDays(2)).Should().BeFalse();
    }

    [Theory]
    [InlineData("ravi@example.com", "ra***@example.com")]
    [InlineData("r@example.com", "r***@example.com")]
    [InlineData("+919812343210", "*********3210")]
    public void The_address_a_code_went_to_is_masked(string contact, string masked)
    {
        SignInSteps.MaskContact(contact).Should().Be(masked);
    }
}

public sealed class VerifySignInCodeCommandValidatorTests
{
    private readonly VerifySignInCodeCommandValidator _validator = new();

    [Theory]
    [InlineData("123456", true)]
    [InlineData("12345", false)]
    [InlineData("12a456", false)]
    [InlineData("", false)]
    public void Wants_six_digits(string code, bool valid)
    {
        _validator.Validate(new VerifySignInCodeCommand("challenge", code, false)).IsValid.Should().Be(valid);
    }
}

public sealed class VerifySignInCodeCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IFailedLoginRecorder _failedLogins = Substitute.For<IFailedLoginRecorder>();
    private readonly ITrustedDeviceRepository _devices = Substitute.For<ITrustedDeviceRepository>();
    private readonly ITokenIssuer _tokenIssuer = Substitute.For<ITokenIssuer>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly VerifySignInCodeCommandHandler _handler;

    private readonly Tenant _tenant;
    private readonly User _user;
    private readonly string _challengeToken;

    /// <summary>
    /// The real plaintext <see cref="LoginOtp.Issue"/> generated - its generator
    /// is a real RandomNumberGenerator this test does not control - and a wrong
    /// code guaranteed different from it.
    /// </summary>
    private readonly string _correctCode;
    private readonly string _wrongCode;

    public VerifySignInCodeCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hash({call.Arg<string>()})");
        _hasher.HashDeterministic(Arg.Any<string>()).Returns(call => $"det({call.Arg<string>()})");

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _user = User.Register(_tenant.Id, "ravi@example.com", "Ravi Shah", "stored-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        var (challenge, token) = LoginChallenge.Issue(_hasher.HashDeterministic, Now, LoginOtp.Lifetime);
        var (otp, plaintext) = LoginOtp.Issue(_hasher.Hash, Now, LoginOtp.Lifetime);
        _challengeToken = token;
        _correctCode = plaintext;
        _wrongCode = plaintext == "999999" ? "000000" : "999999";
        _user.BeginSecondFactor(challenge, otp, plaintext, Now);

        _users.FindByLoginChallengeAsync($"det({token})", Arg.Any<CancellationToken>()).Returns(_user);
        _tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);
        _users.GetAuthorizationAsync(_user.Id, _tenant.Id, Arg.Any<CancellationToken>())
            .Returns(new UserAuthorization(["Member"], ["Members.Read"]));
        _hasher.Verify(_correctCode, $"hash({_correctCode})").Returns(true);
        _tokenIssuer
            .Issue(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(new AccessToken("signed-token", Now.AddHours(1)));
        _sessions.BeginAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(
            new IssuedSession(_user.Id, _tenant.Id, Guid.NewGuid(), "refresh-token", Now.AddDays(14)));

        _handler = new VerifySignInCodeCommandHandler(
            _users, _tenants, _hasher, _failedLogins, _devices, _tokenIssuer, _sessions, _unitOfWork, _clock);
    }

    private Task<Result<SignInResponse>> Verify(string? code = null, string? challenge = null, bool trust = false) =>
        _handler.Handle(
            new VerifySignInCodeCommand(challenge ?? _challengeToken, code ?? _correctCode, trust),
            CancellationToken.None);

    [Fact]
    public async Task The_right_code_signs_in_verifies_the_contact_and_spends_the_challenge()
    {
        var result = await Verify();

        result.IsSuccess.Should().BeTrue();
        result.Value.CodeRequired.Should().BeFalse();
        result.Value.Session!.AccessToken.Should().Be("signed-token");
        result.Value.DeviceToken.Should().BeNull("nobody asked to trust this device");
        _user.IsContactVerified.Should().BeTrue();
        _user.LoginChallenge.Should().BeNull();
        _user.LoginOtp.Should().BeNull();
    }

    [Fact]
    public async Task Asking_to_trust_the_device_hands_back_a_token_for_thirty_days()
    {
        var result = await Verify(trust: true);

        result.Value.DeviceToken.Should().NotBeNullOrEmpty();
        result.Value.DeviceTrustedUntil.Should().Be(Now.Add(TrustedDevice.Lifetime));
        _devices.Received(1).Add(Arg.Is<TrustedDevice>(d =>
            d.UserId == _user.Id && d.TokenHash == $"det({result.Value.DeviceToken})"));
    }

    [Fact]
    public async Task An_unknown_challenge_and_a_wrong_code_are_one_answer()
    {
        var unknown = await Verify(challenge: "not-a-challenge");
        var wrong = await Verify(code: _wrongCode);

        unknown.Error.Should().Be(wrong.Error);
        unknown.Error.Code.Should().Be("Auth.InvalidCode");
    }

    [Fact]
    public async Task A_wrong_code_counts_against_the_lockout_a_wrong_password_uses()
    {
        await Verify(code: _wrongCode);

        await _failedLogins.Received(1).RecordAsync(_user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_expired_challenge_is_refused_and_nothing_is_counted()
    {
        _clock.UtcNow.Returns(Now.AddMinutes(11));

        (await Verify()).Error.Code.Should().Be("Auth.InvalidCode");
        await _failedLogins.DidNotReceive().RecordAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_locked_out_account_is_refused_without_checking_the_code()
    {
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            _user.RecordFailedLogin(Now, User.MaxFailedAttempts, User.LockoutDuration);
        }

        (await Verify()).Error.Code.Should().Be("Auth.LockedOut");
        _hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task An_account_suspended_since_the_password_step_is_refused()
    {
        _user.Suspend(Guid.NewGuid(), Now);

        (await Verify()).Error.Code.Should().Be("Auth.AccountSuspended");
    }

    [Fact]
    public async Task A_Samaaj_deactivated_since_the_password_step_is_refused()
    {
        _tenant.ChangeStatus(TenantStatus.Inactive, Now);

        (await Verify()).Error.Code.Should().Be("Auth.SamaajUnavailable");
    }
}
