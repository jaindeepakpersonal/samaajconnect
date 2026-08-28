using FluentAssertions;
using Microsoft.Extensions.Options;
using Sangam.Gateway.Tenancy;
using Xunit;

namespace Sangam.Gateway.Tests;

public sealed class HostSlugExtractorTests
{
    private readonly HostSlugExtractor _extractor = new(Options.Create(new GatewayOptions
    {
        ApexHosts = ["samaajconnect.com", "www.samaajconnect.com", "localhost"],
        AdminHost = "admin.samaajconnect.com",
    }));

    [Theory]
    [InlineData("mahavir-samaj.samaajconnect.com", "mahavir-samaj")]
    [InlineData("MAHAVIR-SAMAJ.samaajconnect.com", "mahavir-samaj")]
    [InlineData("mahavir-samaj.samaajconnect.com:8080", "mahavir-samaj")]
    [InlineData("mahavir-samaj.samaajconnect.com.", "mahavir-samaj")]
    public void A_Samaaj_subdomain_yields_its_slug(string host, string expected)
    {
        _extractor.Extract(host).Should().Be(expected);
    }

    [Theory]
    [InlineData("samaajconnect.com")]
    [InlineData("www.samaajconnect.com")]
    [InlineData("localhost")]
    [InlineData("localhost:5000")]
    public void An_apex_host_carries_no_Samaaj(string host)
    {
        // Registration and common login both live here, before the member has
        // been routed to a subdomain.
        _extractor.Extract(host).Should().BeNull();
    }

    [Fact]
    public void The_admin_console_carries_no_Samaaj_of_its_own()
    {
        _extractor.Extract("admin.samaajconnect.com").Should().BeNull();
        _extractor.IsAdminHost("admin.samaajconnect.com").Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3:8080")]
    [InlineData("::1")]
    public void An_IP_address_is_never_read_as_a_slug(string host)
    {
        // Otherwise every in-cluster health check would ask identity to resolve
        // a Samaaj called "10".
        _extractor.Extract(host).Should().BeNull();
    }

    [Theory]
    [InlineData("identity-tenant-service")]
    [InlineData("gateway")]
    public void A_single_label_internal_hostname_is_not_a_Samaaj(string host)
    {
        _extractor.Extract(host).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_host_yields_no_slug(string? host)
    {
        _extractor.Extract(host).Should().BeNull();
    }

    [Fact]
    public void A_deeper_subdomain_still_uses_its_first_label()
    {
        _extractor.Extract("mahavir-samaj.in.samaajconnect.com").Should().Be("mahavir-samaj");
    }

    [Fact]
    public void Only_the_configured_admin_host_counts_as_the_admin_console()
    {
        _extractor.IsAdminHost("admin.example.com").Should().BeFalse();
        _extractor.IsAdminHost("mahavir-samaj.samaajconnect.com").Should().BeFalse();
    }
}
