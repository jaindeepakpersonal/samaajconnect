using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Consents.Commands.EraseMyAccount;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class UserErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static User Register() =>
        User.Register(TenantId, "ravi@example.com", "Ravi Shah", "hash",
            AuthorizationCatalog.RoleIds.Member, Now);

    [Fact]
    public void Erase_removes_every_field_that_names_the_person()
    {
        var user = Register();

        user.Erase(Now);

        user.FullName.Should().NotContain("Ravi");
        user.MobileOrEmail.Should().NotContain("ravi@example.com");
        user.PasswordHash.Should().BeEmpty();
        user.Status.Should().Be(UserStatus.Erased);
    }

    [Fact]
    public void Erase_leaves_an_identifier_that_is_unique_and_cannot_be_signed_in_to()
    {
        // The column is unique platform-wide, so two erased accounts must not
        // collide - and the replacement must not be a value anyone could
        // register or receive a code at.
        var first = Register();
        var second = User.Register(TenantId, "meena@example.com", "Meena Shah", "hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        first.Erase(Now);
        second.Erase(Now);

        first.MobileOrEmail.Should().NotBe(second.MobileOrEmail);
        first.MobileOrEmail.Should().EndWith("@invalid");
    }

    [Fact]
    public void Erase_drops_the_roles_so_a_stale_token_grants_nothing()
    {
        var user = Register();

        user.Erase(Now);

        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void Erase_clears_an_outstanding_activation_code()
    {
        var user = Register();
        user.Erase(Now);

        user.ActivationCode.Should().BeNull();
    }

    [Fact]
    public void Erase_raises_UserErased_for_the_other_services_to_act_on()
    {
        var user = Register();
        user.ClearDomainEvents();

        user.Erase(Now);

        var raised = user.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<UserErasedDomainEvent>().Subject;

        raised.UserId.Should().Be(user.Id);
        raised.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Erasing_twice_is_a_no_op_because_delivery_is_at_least_once()
    {
        var user = Register();
        user.Erase(Now);
        var identifier = user.MobileOrEmail;
        user.ClearDomainEvents();

        user.Erase(Now.AddDays(1));

        user.MobileOrEmail.Should().Be(identifier);
        user.DomainEvents.Should().BeEmpty();
    }
}

public sealed class EraseMyAccountCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly EraseMyAccountCommandHandler _handler;
    private readonly User _user;

    public EraseMyAccountCommandHandlerTests()
    {
        _user = User.Register(TenantId, "ravi@example.com", "Ravi Shah", "hash",
            AuthorizationCatalog.RoleIds.Member, Now);

        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(_user.Id);
        _hasher.Verify("correct-horse", "hash").Returns(true);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

        _handler = new EraseMyAccountCommandHandler(
            _users, _hasher, _unitOfWork, _currentUser, _clock,
            NullLogger<EraseMyAccountCommandHandler>.Instance);
    }

    private Task<Application.Common.Result<EraseMyAccountResponse>> Handle(string password) =>
        _handler.Handle(new EraseMyAccountCommand(password), CancellationToken.None);

    [Fact]
    public async Task Erases_the_account_when_the_password_is_right()
    {
        var result = await Handle("correct-horse");

        result.IsSuccess.Should().BeTrue();
        _user.Status.Should().Be(UserStatus.Erased);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_wrong_password_and_leaves_the_account_alone()
    {
        var result = await Handle("guess");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.InvalidCredentials");
        _user.Status.Should().Be(UserStatus.Active);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_platform_administrator()
    {
        // Nothing can recreate a Super Admin except the bootstrap on an empty
        // database, so this would leave a platform nobody can administer.
        _currentUser.IsInRole(Roles.SuperAdmin).Returns(true);

        var result = await Handle("correct-horse");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Erasure.PlatformAdmin");
        _user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task Tells_the_member_what_survives_rather_than_just_saying_done()
    {
        var result = await Handle("correct-horse");

        result.Value.WhatWasErased.Should().NotBeEmpty();
        result.Value.WhatIsKeptAndWhy.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Fails_cleanly_when_the_account_is_already_gone()
    {
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Handle("correct-horse");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.NotFound");
    }
}
