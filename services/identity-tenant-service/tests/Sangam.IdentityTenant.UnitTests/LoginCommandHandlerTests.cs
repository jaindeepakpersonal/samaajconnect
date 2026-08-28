using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Users.Commands.Login;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly LoginCommandHandler _handler;

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

        _handler = new LoginCommandHandler(
            _users, _tenants, _passwordHasher, _failedLogins, _tokenIssuer, _unitOfWork, _clock);
    }

    private Task<Result<Application.Users.LoginResponse>> Login(
        string identifier = "Ravi@Example.com", string password = "right") =>
        _handler.Handle(new LoginCommand(identifier, password), CancellationToken.None);

    [Fact]
    public async Task A_correct_password_returns_a_token_and_the_Samaaj_to_redirect_to()
    {
        var result = await Login();

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("signed-token");
        result.Value.TenantSlug.Should().Be("mumbai");
        result.Value.Roles.Should().ContainSingle().Which.Should().Be("Member");
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
            _user.RecordFailedLogin(Now);
        }

        var result = await Login();

        result.Error.Code.Should().Be("Auth.LockedOut");
        _passwordHasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_suspended_account_cannot_log_in_even_with_the_right_password()
    {
        _user.Suspend();

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
}
