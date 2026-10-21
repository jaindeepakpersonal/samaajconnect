using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Users.Commands.Login;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IFailedLoginRecorder _failedLogins = Substitute.For<IFailedLoginRecorder>();
    private readonly ITokenIssuer _tokenIssuer = Substitute.For<ITokenIssuer>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICaptchaStore _captchas = Substitute.For<ICaptchaStore>();
    private readonly ITrustedDeviceRepository _devices = Substitute.For<ITrustedDeviceRepository>();
    private readonly ISecurityPolicyResolver _policy = Substitute.For<ISecurityPolicyResolver>();

    /// <summary>
    /// Both guards off for the password-path tests that were here first; the
    /// guard tests below switch them on. See SignInSecurityOptions.
    /// </summary>
    private readonly SignInSecurityOptions _options = new() { CaptchaRequired = false, TwoFactorRequired = false };

    private LoginCommandHandler _handler;

    private readonly Tenant _tenant;
    private readonly User _user;

    public LoginCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _user = User.Register(
            _tenant.Id, "ravi@example.com", "Ravi Shah", "stored-hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        _users.FindForLoginAsync("ravi@example.com", Arg.Any<CancellationToken>()).Returns(_user);
        _tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);
        _users.GetAuthorizationAsync(_user.Id, _tenant.Id, Arg.Any<CancellationToken>())
            .Returns(new UserAuthorization(["Member"], ["Members.Read"]));
        _passwordHasher.Verify("right", "stored-hash").Returns(true);
        _tokenIssuer
            .Issue(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(new AccessToken("signed-token", Now.AddHours(1)));

        _sessions.BeginAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(
            new IssuedSession(_user.Id, _tenant.Id, Guid.NewGuid(), "refresh-token", Now.AddDays(14)));

        _passwordHasher.Hash(Arg.Any<string>()).Returns(call => $"hash({call.Arg<string>()})");
        _passwordHasher.HashDeterministic(Arg.Any<string>()).Returns(call => $"det({call.Arg<string>()})");
        _policy.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new ResolvedSecurityPolicy(10, LoginOtp.Lifetime, User.MaxFailedAttempts, User.LockoutDuration, TimeSpan.FromDays(14)));

        _handler = Handler();
    }

    private LoginCommandHandler Handler() => new(
        _users, _tenants, _passwordHasher, _failedLogins, _captchas, _devices, _tokenIssuer, _sessions,
        _unitOfWork, _clock, _policy, _options, NullLogger<LoginCommandHandler>.Instance);

    private Task<Result<SignInResponse>> Login(
        string identifier = "Ravi@Example.com",
        string password = "right",
        Guid? captchaId = null,
        string? captchaAnswer = null,
        string? deviceToken = null) =>
        _handler.Handle(new LoginCommand(identifier, password, captchaId, captchaAnswer, deviceToken), CancellationToken.None);

    [Fact]
    public async Task A_correct_password_returns_a_token_and_the_Samaaj_to_redirect_to()
    {
        var result = await Login();

        result.IsSuccess.Should().BeTrue();
        result.Value.Session!.AccessToken.Should().Be("signed-token");
        result.Value.Session.TenantSlug.Should().Be("mumbai");
        result.Value.Session.Roles.Should().ContainSingle().Which.Should().Be("Member");
    }

    [Fact]
    public async Task A_successful_login_is_recorded_and_saved()
    {
        await Login();

        _user.LastLoginAt.Should().Be(Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_identifier_is_normalised_before_lookup()
    {
        await Login(identifier: "  RAVI@example.COM ");

        await _users.Received().FindForLoginAsync("ravi@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_password_are_indistinguishable()
    {
        _users.FindForLoginAsync("ghost@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var unknown = await Login(identifier: "ghost@example.com");
        var wrongPassword = await Login(password: "wrong");

        unknown.Error.Should().Be(wrongPassword.Error);
        unknown.Error.Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task A_wrong_password_is_recorded_outside_the_rolled_back_transaction()
    {
        await Login(password: "wrong");

        // The handler returns a failure, so TransactionBehavior will roll back;
        // the attempt must be recorded through the separate recorder instead.
        await _failedLogins.Received(1).RecordAsync(_user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_locked_out_account_is_refused_without_checking_the_password()
    {
        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            _user.RecordFailedLogin(Now, User.MaxFailedAttempts, User.LockoutDuration);
        }

        var result = await Login();

        result.Error.Code.Should().Be("Auth.LockedOut");
        _passwordHasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_suspended_account_cannot_log_in_even_with_the_right_password()
    {
        _user.Suspend(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var result = await Login();

        result.Error.Code.Should().Be("Auth.AccountSuspended");
    }

    [Fact]
    public async Task A_member_of_a_deactivated_Samaaj_cannot_log_in()
    {
        _tenant.ChangeStatus(TenantStatus.Inactive, Now);

        var result = await Login();

        result.Error.Code.Should().Be("Auth.SamaajUnavailable");
    }

    [Fact]
    public async Task No_token_is_issued_for_a_failed_login()
    {
        await Login(password: "wrong");

        _tokenIssuer.DidNotReceive().Issue(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>());
    }

    // ---- The guards ----------------------------------------------------------------

    [Fact]
    public async Task With_two_factor_on_a_correct_password_sends_a_code_and_signs_nobody_in()
    {
        _options.TwoFactorRequired = true;
        _user.ClearDomainEvents();

        var result = await Login();

        result.IsSuccess.Should().BeTrue();
        result.Value.CodeRequired.Should().BeTrue();
        result.Value.Session.Should().BeNull();
        result.Value.ChallengeToken.Should().NotBeNullOrEmpty();
        result.Value.CodeSentTo.Should().Be("ra***@example.com");
        result.Value.DevCode.Should().BeNull("RevealSecretsInLogs is off by default");

        _user.LoginChallenge!.Hash.Should().Be($"det({result.Value.ChallengeToken})");
        _user.DomainEvents.Should().ContainSingle(e => e is LoginOtpRequestedDomainEvent);
        _user.LastLoginAt.Should().BeNull("a sign-in waiting on its code has not happened yet");
        _tokenIssuer.DidNotReceive().Issue(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>());
    }

    /// <summary>Local compose only - see SignInSecurityOptions.RevealSecretsInLogs.</summary>
    [Fact]
    public async Task RevealSecretsInLogs_puts_the_plaintext_code_on_the_response()
    {
        _options.TwoFactorRequired = true;
        _options.RevealSecretsInLogs = true;

        var result = await Login();

        result.Value.DevCode.Should().MatchRegex("^[0-9]{6}$");
    }

    [Fact]
    public async Task A_device_this_account_trusts_skips_the_code_but_not_the_password()
    {
        _options.TwoFactorRequired = true;
        var (device, token) = TrustedDevice.Issue(_user.Id, _passwordHasher.HashDeterministic, Now);
        _devices.FindUsableAsync(_user.Id, $"det({token})", Arg.Any<CancellationToken>()).Returns(device);

        var trusted = await Login(deviceToken: token);

        trusted.Value.CodeRequired.Should().BeFalse();
        trusted.Value.Session!.AccessToken.Should().Be("signed-token");
        device.LastUsedAt.Should().Be(Now);

        (await Login(password: "wrong", deviceToken: token)).Error.Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task An_unknown_device_token_still_gets_a_code()
    {
        _options.TwoFactorRequired = true;

        (await Login(deviceToken: "somebody-elses")).Value.CodeRequired.Should().BeTrue();
    }

    [Fact]
    public async Task With_the_CAPTCHA_on_a_missing_or_wrong_answer_is_refused_before_any_account_is_read()
    {
        _options.CaptchaRequired = true;
        var captchaId = Guid.NewGuid();
        _captchas.TryRedeemAsync(captchaId, "AB3CD", Arg.Any<CancellationToken>()).Returns(true);

        (await Login()).Error.Code.Should().Be("Auth.CaptchaFailed");
        (await Login(captchaId: captchaId, captchaAnswer: "WRONG")).Error.Code.Should().Be("Auth.CaptchaFailed");
        await _users.DidNotReceive().FindForLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        (await Login(captchaId: captchaId, captchaAnswer: "AB3CD")).IsSuccess.Should().BeTrue();
    }
}
