using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Behaviors;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class TenantAuthorizationBehaviorTests
{
    [AllowAnonymousRequest]
    public sealed record AnonymousRequest : IQuery<string>;

    [RequiresRoles(Roles.SuperAdmin)]
    public sealed record RoleGuardedRequest : ICommand<string>;

    [RequiresPermission(PermissionKeys.TenantManage)]
    public sealed record PermissionGuardedRequest : ICommand<string>;

    public sealed record UnannotatedRequest : ICommand<string>;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    private static readonly RequestHandlerDelegate<Result<string>> Next =
        () => Task.FromResult(Result.Success("handled"));

    private TenantAuthorizationBehavior<TRequest, Result<string>> BehaviorFor<TRequest>()
        where TRequest : notnull =>
        new(_currentUser, _tenantContext, NullLogger<TenantAuthorizationBehavior<TRequest, Result<string>>>.Instance);

    [Fact]
    public async Task Lets_an_explicitly_anonymous_request_through_without_a_user()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await BehaviorFor<AnonymousRequest>()
            .Handle(new AnonymousRequest(), Next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_an_unauthenticated_caller_on_a_guarded_request()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await BehaviorFor<RoleGuardedRequest>()
            .Handle(new RoleGuardedRequest(), Next, CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Fails_closed_when_a_request_declares_no_policy_at_all()
    {
        _currentUser.IsAuthenticated.Returns(true);

        var result = await BehaviorFor<UnannotatedRequest>()
            .Handle(new UnannotatedRequest(), Next, CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("Auth.NoPolicy");
    }

    [Fact]
    public async Task Rejects_a_caller_missing_the_required_role()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Roles.SuperAdmin).Returns(false);

        var result = await BehaviorFor<RoleGuardedRequest>()
            .Handle(new RoleGuardedRequest(), Next, CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("Auth.Forbidden");
    }

    [Fact]
    public async Task Admits_a_caller_holding_the_required_role()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Roles.SuperAdmin).Returns(true);

        var result = await BehaviorFor<RoleGuardedRequest>()
            .Handle(new RoleGuardedRequest(), Next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_a_caller_missing_the_required_permission()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.HasPermission(PermissionKeys.TenantManage).Returns(false);

        var result = await BehaviorFor<PermissionGuardedRequest>()
            .Handle(new PermissionGuardedRequest(), Next, CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Refuses_a_tenant_override_from_a_caller_who_is_not_a_Super_Admin()
    {
        _tenantContext.IsOverride.Returns(true);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Roles.SuperAdmin).Returns(false);

        var result = await BehaviorFor<PermissionGuardedRequest>()
            .Handle(new PermissionGuardedRequest(), Next, CancellationToken.None);

        result.Error.Code.Should().Be("Auth.OverrideDenied");
    }

    [Fact]
    public async Task Refuses_a_tenant_override_on_an_otherwise_anonymous_request()
    {
        _tenantContext.IsOverride.Returns(true);
        _currentUser.IsAuthenticated.Returns(false);

        var result = await BehaviorFor<AnonymousRequest>()
            .Handle(new AnonymousRequest(), Next, CancellationToken.None);

        result.Error.Code.Should().Be("Auth.OverrideDenied");
    }

    [Fact]
    public async Task Allows_a_tenant_override_from_a_Super_Admin()
    {
        _tenantContext.IsOverride.Returns(true);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Roles.SuperAdmin).Returns(true);
        _currentUser.HasPermission(PermissionKeys.TenantManage).Returns(true);

        var result = await BehaviorFor<PermissionGuardedRequest>()
            .Handle(new PermissionGuardedRequest(), Next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Admits_a_caller_holding_the_required_permission()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.HasPermission(PermissionKeys.TenantManage).Returns(true);

        var result = await BehaviorFor<PermissionGuardedRequest>()
            .Handle(new PermissionGuardedRequest(), Next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
