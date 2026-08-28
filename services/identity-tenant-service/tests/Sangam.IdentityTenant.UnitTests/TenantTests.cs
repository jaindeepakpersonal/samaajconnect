using FluentAssertions;
using Sangam.IdentityTenant.Domain.Tenants;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalises_the_slug_so_casing_cannot_fork_a_tenant()
    {
        var tenant = Tenant.Create("Mumbai Samaaj", "  Mumbai-Samaaj ", null, null, null, null, Now);

        tenant.Slug.Should().Be("mumbai-samaaj");
    }

    [Fact]
    public void Create_starts_a_tenant_inactive_so_creation_and_go_live_stay_separate_decisions()
    {
        var tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);

        tenant.Status.Should().Be(TenantStatus.Inactive);
    }

    [Fact]
    public void Create_raises_a_TenantCreated_event_carrying_the_new_tenant_id()
    {
        var tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);

        var raised = tenant.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TenantCreatedDomainEvent>().Subject;

        raised.TenantId.Should().Be(tenant.Id);
        raised.Slug.Should().Be("mumbai");
        raised.Topic.Should().Be("identity.tenant.created.v1");
        raised.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void Create_deduplicates_enabled_modules_case_insensitively()
    {
        var tenant = Tenant.Create(
            "Mumbai Samaaj", "mumbai", null, null, null, ["Pathshala", "pathshala", " Boli "], Now);

        tenant.EnabledModules.Should().BeEquivalentTo(["Pathshala", "Boli"]);
    }

    [Fact]
    public void Create_lowercases_domain_and_contact_email()
    {
        var tenant = Tenant.Create(
            "Mumbai Samaaj", "mumbai", "Mumbai.Example.COM", " Ravi Shah ", " Ravi@Example.COM ", null, Now);

        tenant.Domain.Should().Be("mumbai.example.com");
        tenant.ContactEmail.Should().Be("ravi@example.com");
        tenant.ContactPerson.Should().Be("Ravi Shah");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => Tenant.Create(name, "mumbai", null, null, null, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ClearDomainEvents_empties_the_list_so_events_are_not_published_twice()
    {
        var tenant = Tenant.Create("Mumbai Samaaj", "mumbai", null, null, null, null, Now);

        tenant.ClearDomainEvents();

        tenant.DomainEvents.Should().BeEmpty();
    }
}
