using FluentAssertions;
using Sangam.MemberFamily.Domain.Common;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// What the platform accepts as a photo link. See <see cref="ImageUrl"/> for
/// why length alone was not enough.
/// </summary>
public sealed class ImageUrlTests
{
    [Theory]
    [InlineData("https://cdn.example.com/photo.jpg")]
    [InlineData("http://cdn.example.com/photo.jpg")]
    public void An_absolute_web_address_is_accepted(string value)
    {
        ImageUrl.IsAcceptable(value).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_photo_is_fine(string? value)
    {
        ImageUrl.IsAcceptable(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    public void A_scripted_or_inline_link_is_refused(string value)
    {
        // Rendered into a page this is stored cross-site scripting. Angular's
        // sanitiser would refuse it, but the API serves this value to anything
        // that asks and cannot assume its consumer sanitises.
        ImageUrl.IsAcceptable(value).Should().BeFalse();
    }

    [Theory]
    [InlineData("/photos/me.jpg")]
    [InlineData("photo.jpg")]
    [InlineData("//cdn.example.com/photo.jpg")]
    public void A_relative_or_scheme_less_link_is_refused(string value)
    {
        // Resolved against whatever page happens to render it, which is a
        // different address in each app.
        ImageUrl.IsAcceptable(value).Should().BeFalse();
    }

    [Fact]
    public void An_absurdly_long_link_is_refused()
    {
        ImageUrl.IsAcceptable("https://example.com/" + new string('a', 3000))
            .Should().BeFalse();
    }

    [Fact]
    public void Any_host_is_still_allowed_and_that_is_a_known_gap()
    {
        // This check closes the scripting hole, not the tracking one: a photo
        // hosted anywhere still sends every viewer's IP to that host, which on
        // a child's profile is what DPDP s.9(3) is about. The fix is the
        // platform hosting its own images; until then this test states the
        // limitation rather than letting it be discovered later.
        ImageUrl.IsAcceptable("https://tracker.example.net/1x1.gif").Should().BeTrue();
    }
}
