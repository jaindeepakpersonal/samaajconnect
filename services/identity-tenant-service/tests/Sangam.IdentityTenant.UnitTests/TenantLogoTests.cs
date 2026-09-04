using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sangam.IdentityTenant.Domain.Media;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class TenantLogoTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static TenantLogo Capture(byte[]? bytes = null) =>
        TenantLogo.Capture(TenantId, bytes ?? Png(), ActorId, Now);

    /// <summary>
    /// The reason sniffing lives in the domain rather than at the endpoint: the
    /// type served back is one the platform derived, and there is no parameter
    /// by which an uploader could have supplied it.
    /// </summary>
    [Fact]
    public void The_content_type_comes_from_the_bytes_and_cannot_be_supplied()
    {
        Capture().ContentType.Should().Be(ImageContent.Png);

        typeof(TenantLogo)
            .GetMethod(nameof(TenantLogo.Capture))!
            .GetParameters()
            .Select(p => p.Name)
            .Should().NotContain("contentType");
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_bytes_it_stored()
    {
        var bytes = Png();
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var logo = Capture(bytes);

        logo.ContentHash.Should().Be(expected);
        logo.ByteSize.Should().Be(bytes.Length);
    }

    [Fact]
    public void Bytes_that_are_not_an_image_cannot_become_a_logo()
    {
        var act = () => TenantLogo.Capture(
            TenantId, Encoding.UTF8.GetBytes("not a picture"), ActorId, Now);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// An SVG is a document that can carry script, and a logo is served from
    /// the platform's own origin to anybody at all - which makes accepting one
    /// worse here than anywhere else on the platform.
    /// </summary>
    [Fact]
    public void An_svg_cannot_become_a_logo()
    {
        var svg = Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>");

        ImageContent.Sniff(svg).Should().BeNull();
    }
}
