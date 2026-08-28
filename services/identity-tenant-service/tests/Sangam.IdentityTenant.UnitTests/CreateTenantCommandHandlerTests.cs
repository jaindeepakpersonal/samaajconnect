using FluentAssertions;
using NSubstitute;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;
using Sangam.IdentityTenant.Domain.Tenants;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class CreateTenantCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CreateTenantCommandHandler _handler;

    public CreateTenantCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _handler = new CreateTenantCommandHandler(_tenants, _unitOfWork, _clock);
    }

    private static CreateTenantCommand Command(
        string name = "Mumbai Samaaj",
        string slug = "mumbai",
        string? domain = null) =>
        new(name, slug, domain, "Ravi Shah", "ravi@example.com", ["Pathshala"]);

    [Fact]
    public async Task Creates_the_tenant_and_saves_once()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Slug.Should().Be("mumbai");
        result.Value.Status.Should().Be(nameof(TenantStatus.Inactive));

        _tenants.Received(1).Add(Arg.Is<Tenant>(t => t.Slug == "mumbai"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Normalises_the_slug_before_checking_uniqueness()
    {
        await _handler.Handle(Command(slug: "  MUMBAI "), CancellationToken.None);

        await _tenants.Received(1).SlugExistsAsync("mumbai", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_a_conflict_rather_than_throwing_when_the_slug_is_taken()
    {
        _tenants.SlugExistsAsync("mumbai", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Tenant.SlugTaken");

        _tenants.DidNotReceive().Add(Arg.Any<Tenant>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_a_conflict_when_the_custom_domain_is_already_mapped()
    {
        _tenants.DomainExistsAsync("mumbai.example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(
            Command(domain: "Mumbai.Example.com"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.DomainTaken");
    }

    [Fact]
    public async Task Does_not_check_domain_uniqueness_when_no_domain_was_supplied()
    {
        await _handler.Handle(Command(domain: null), CancellationToken.None);

        await _tenants.DidNotReceive().DomainExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stamps_CreatedAt_from_the_injected_clock()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.CreatedAt.Should().Be(Now);
    }
}
