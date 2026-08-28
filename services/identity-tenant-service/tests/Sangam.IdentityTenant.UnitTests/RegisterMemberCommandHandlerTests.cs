using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Users.Commands.RegisterMember;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class RegisterMemberCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IConsentRepository _consents = Substitute.For<IConsentRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly RegisterMemberCommandHandler _handler;

    private readonly Tenant _tenant;

    public RegisterMemberCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _passwordHasher.Hash(Arg.Any<string>()).Returns("hashed");

        _tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);
        _tenant.ChangeStatus(TenantStatus.Active, Now);

        _tenants.GetBySlugAsync("mumbai", Arg.Any<CancellationToken>()).Returns(_tenant);

        _handler = new RegisterMemberCommandHandler(
            _tenants, _users, _passwordHasher, _unitOfWork, _tenantContext, _consents, _clock);
    }

    private Task<Result<Application.Users.RegisterMemberResponse>> Register(
        string slug = "Mumbai", string identifier = "Ravi@Example.com") =>
        _handler.Handle(
            new RegisterMemberCommand(
                slug,
                "Ravi Shah",
                identifier,
                "a-long-enough-password",
                ["Membership"],
                "2026-08-28.1"),
            CancellationToken.None);

    [Fact]
    public async Task Registers_the_member_into_the_selected_Samaaj()
    {
        var result = await Register();

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(_tenant.Id);
        result.Value.TenantSlug.Should().Be("mumbai");
        result.Value.MobileOrEmail.Should().Be("ravi@example.com");

        _users.Received(1).Add(Arg.Is<User>(u => u.TenantId == _tenant.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_stored_password_is_the_hash_and_never_the_plaintext()
    {
        await Register();

        _passwordHasher.Received(1).Hash("a-long-enough-password");
        _users.Received(1).Add(Arg.Is<User>(u => u.PasswordHash == "hashed"));
    }

    [Fact]
    public async Task An_unknown_Samaaj_is_a_not_found()
    {
        _tenants.GetBySlugAsync("ghost", Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var result = await Register(slug: "ghost");

        result.Error.Type.Should().Be(ErrorType.NotFound);
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task A_Samaaj_that_has_not_been_activated_yet_does_not_accept_registrations()
    {
        _tenant.ChangeStatus(TenantStatus.Inactive, Now);

        var result = await Register();

        result.Error.Code.Should().Be("Tenant.NotActive");
    }

    [Fact]
    public async Task An_archived_Samaaj_is_reported_as_missing_rather_than_as_archived()
    {
        _tenant.ChangeStatus(TenantStatus.Archived, Now);

        var result = await Register();

        result.Error.Code.Should().Be("Tenant.NotFound");
    }

    [Fact]
    public async Task Registering_on_one_Samaaj_subdomain_cannot_create_a_member_in_another()
    {
        _tenantContext.TenantId.Returns(Guid.NewGuid());

        var result = await Register();

        result.Error.Code.Should().Be("Tenant.Mismatch");
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Registering_on_the_matching_subdomain_is_allowed()
    {
        _tenantContext.TenantId.Returns(_tenant.Id);

        (await Register()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_identifier_already_used_anywhere_on_the_platform_is_rejected()
    {
        _users.IdentifierExistsAsync("ravi@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await Register();

        result.Error.Code.Should().Be("User.IdentifierTaken");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
