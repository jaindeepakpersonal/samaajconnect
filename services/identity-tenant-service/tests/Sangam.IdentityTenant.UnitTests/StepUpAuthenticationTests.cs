using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Application.Tenants;
using Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

/// <summary>
/// Re-asking for the password before something irreversible.
/// </summary>
public sealed class StepUpAuthenticationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly StepUpAuthentication _stepUp;
    private readonly User _superAdmin;

    public StepUpAuthenticationTests()
    {
        // A Super Admin's account lives at PlatformTenantId, not in any Samaaj.
        _superAdmin = User.Register(
            User.PlatformTenantId, "root@samaajconnect.local", "Root", "hash",
            AuthorizationCatalog.RoleIds.SuperAdmin, Now);

        _currentUser.UserId.Returns(_superAdmin.Id);
        _hasher.Verify("correct-horse", "hash").Returns(true);
        _users.GetSelfAsync(_superAdmin.Id, Arg.Any<CancellationToken>()).Returns(_superAdmin);

        _stepUp = new StepUpAuthentication(_users, _hasher, _currentUser);
    }

    [Fact]
    public async Task Accepts_the_right_password()
    {
        var result = await _stepUp.ConfirmAsync("correct-horse", "Doing the thing");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_a_wrong_password_with_403_rather_than_401()
    {
        // Load-bearing, and not a style choice. The portals' interceptor treats
        // a 401 as an expired access token: it renews the token and retries the
        // original request. On the endpoints that ask for a step-up, that means
        // resubmitting a destructive command because somebody mistyped.
        var result = await _stepUp.ConfirmAsync("guess", "Doing the thing");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(IStepUpAuthentication.StepUpFailedCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Refuses_a_missing_password_the_same_way_as_a_wrong_one(string? password)
    {
        var result = await _stepUp.ConfirmAsync(password, "Doing the thing");

        result.Error.Code.Should().Be(IStepUpAuthentication.StepUpFailedCode);
    }

    [Fact]
    public async Task Reads_the_account_past_the_tenant_filter()
    {
        await _stepUp.ConfirmAsync("correct-horse", "Doing the thing");

        // GetByIdAsync is tenant-filtered, and a Super Admin acting on a Samaaj
        // carries that Samaaj in ITenantContext while their own account sits
        // outside it. Reading through the filter would find nothing and fail
        // the step-up for the one role that most needs it - the same trap that
        // once made /me answer 404 for an overriding Super Admin.
        await _users.Received(1).GetSelfAsync(_superAdmin.Id, Arg.Any<CancellationToken>());
        await _users.DidNotReceive().GetByIdAsync(_superAdmin.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_unauthenticated_caller()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await _stepUp.ConfirmAsync("correct-horse", "Doing the thing");

        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}

/// <summary>
/// Which status changes have to be confirmed, and which do not.
/// </summary>
public sealed class ChangeTenantStatusStepUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ChangeTenantStatusCommandHandler _handler;
    private readonly Tenant _tenant;

    public ChangeTenantStatusStepUpTests()
    {
        var superAdmin = User.Register(
            User.PlatformTenantId, "root@samaajconnect.local", "Root", "hash",
            AuthorizationCatalog.RoleIds.SuperAdmin, Now);

        // A new Samaaj starts Inactive; these tests are about taking a serving
        // one out of service, so it is activated first.
        _tenant = Tenant.Create("Udaipur Samaaj", "udaipur-samaaj", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(superAdmin.Id);
        _hasher.Verify("correct-horse", "hash").Returns(true);
        _users.GetSelfAsync(superAdmin.Id, Arg.Any<CancellationToken>()).Returns(superAdmin);
        _tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);

        _handler = new ChangeTenantStatusCommandHandler(
            _tenants,
            new StepUpAuthentication(_users, _hasher, _currentUser),
            _unitOfWork,
            _clock,
            _currentUser,
            NullLogger<ChangeTenantStatusCommandHandler>.Instance);
    }

    private Task<Result<TenantResponse>> Handle(string status, string? password = null) =>
        _handler.Handle(
            new ChangeTenantStatusCommand(_tenant.Id, status, password), CancellationToken.None);

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task Taking_a_Samaaj_out_of_service_needs_the_password(string status)
    {
        var result = await Handle(status);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IStepUpAuthentication.StepUpFailedCode);
        _tenant.Status.Should().Be(TenantStatus.Active);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task And_goes_through_with_the_right_one(string status)
    {
        var result = await Handle(status, "correct-horse");

        result.IsSuccess.Should().BeTrue();
        _tenant.Status.Should().Be(Enum.Parse<TenantStatus>(status));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        var result = await Handle("Inactive", "guess");

        result.Error.Code.Should().Be(IStepUpAuthentication.StepUpFailedCode);
        _tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task Bringing_one_back_into_service_does_not()
    {
        // The asymmetry is deliberate. Activating restores service and is
        // undone by the very call that undid it; asking for a password on the
        // harmless direction only teaches people to type it without reading
        // the screen.
        _tenant.ChangeStatus(TenantStatus.Inactive, Now);

        var result = await Handle("Active");

        result.IsSuccess.Should().BeTrue();
        _tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task An_archived_Samaaj_is_refused_before_the_password_is_asked_for()
    {
        // A request that cannot succeed should say so rather than first
        // demanding a credential. Nothing is given away: the caller is a Super
        // Admin who can already read this Samaaj's status.
        _tenant.ChangeStatus(TenantStatus.Archived, Now);

        var result = await Handle("Inactive");

        result.Error.Code.Should().Be("Tenant.Archived");
    }
}
