using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sangam.MemberFamily.Domain.Media;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// What the platform will accept as a photo, and what it does with it.
/// </summary>
/// <remarks>
/// These are bytes rather than files on purpose. Every check here is about the
/// content of an upload, and a fixture that read a real .jpg off disk would be
/// testing that the file was still there.
/// </remarks>
public sealed class ImageContentTests
{
    private static byte[] Jpeg(int extra = 32) =>
        [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[extra]];

    private static byte[] Png(int extra = 32) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[extra]];

    private static byte[] Webp(int extra = 32) =>
        [
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x20, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P',
            .. new byte[extra],
        ];

    [Fact]
    public void A_jpeg_is_recognised_by_its_bytes()
    {
        ImageContent.Sniff(Jpeg()).Should().Be(ImageContent.Jpeg);
    }

    [Fact]
    public void A_png_is_recognised_by_its_full_eight_byte_signature()
    {
        ImageContent.Sniff(Png()).Should().Be(ImageContent.Png);

        // One byte of the signature wrong is not a PNG. The tail of that
        // signature exists to catch a mangled transfer, so a check that only
        // looked at the first four bytes would pass a file that is not one.
        var mangled = Png();
        mangled[5] = 0x00;

        ImageContent.Sniff(mangled).Should().BeNull();
    }

    [Fact]
    public void A_webp_needs_both_the_riff_container_and_the_webp_form()
    {
        ImageContent.Sniff(Webp()).Should().Be(ImageContent.Webp);

        // A RIFF file that is not a WebP - a .wav, for instance - shares the
        // first four bytes and is not an image.
        var wav = Webp();
        wav[8] = (byte)'W';
        wav[9] = (byte)'A';
        wav[10] = (byte)'V';
        wav[11] = (byte)'E';

        ImageContent.Sniff(wav).Should().BeNull();
    }

    /// <summary>
    /// The exclusion that matters most. An SVG is a document that can carry
    /// script, and this platform serves stored images back from its own origin -
    /// so accepting one would close the third-party tracking hole and open a
    /// stored cross-site scripting hole in its place.
    /// </summary>
    [Fact]
    public void An_svg_is_not_an_image_this_platform_will_store()
    {
        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        ImageContent.Sniff(svg).Should().BeNull();
    }

    [Fact]
    public void Something_that_is_not_an_image_at_all_is_refused()
    {
        ImageContent.Sniff(Encoding.UTF8.GetBytes("PK this is a zip"))
            .Should().BeNull();
        ImageContent.Sniff(Encoding.UTF8.GetBytes("%PDF-1.7")).Should().BeNull();
        ImageContent.Sniff([]).Should().BeNull();
    }

    [Fact]
    public void An_image_over_the_cap_is_refused_however_well_formed_it_is()
    {
        var oversized = Jpeg(ImageContent.MaxBytes);

        ImageContent.Sniff(oversized).Should().BeNull();

        // And the one just under it is not, so the cap is a boundary rather
        // than an approximation.
        var largest = Jpeg(ImageContent.MaxBytes - 4);

        largest.Length.Should().Be(ImageContent.MaxBytes);
        ImageContent.Sniff(largest).Should().Be(ImageContent.Jpeg);
    }
}

public sealed class StoredImageTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static StoredImage Capture(byte[]? bytes = null) => StoredImage.Capture(
        TenantId, ImageOwnerKind.Member, OwnerId, bytes ?? Png(), ActorId, Now);

    /// <summary>
    /// The whole reason sniffing lives in the domain rather than at the
    /// endpoint: the type served back to a browser is one the platform derived
    /// from the bytes, and there is no parameter by which an uploader could
    /// have supplied it.
    /// </summary>
    [Fact]
    public void The_content_type_comes_from_the_bytes_and_cannot_be_supplied()
    {
        Capture().ContentType.Should().Be(ImageContent.Png);

        typeof(StoredImage)
            .GetMethod(nameof(StoredImage.Capture))!
            .GetParameters()
            .Select(p => p.Name)
            .Should().NotContain("contentType");
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_bytes_it_stored()
    {
        var bytes = Png();
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var image = Capture(bytes);

        image.ContentHash.Should().Be(expected);
        image.ByteSize.Should().Be(bytes.Length);
    }

    [Fact]
    public void Two_uploads_of_the_same_picture_hash_the_same_and_are_still_two_rows()
    {
        var first = Capture();
        var second = Capture();

        // The ETag is about the bytes, so it matches. The identity is not, so a
        // member replacing their photo with the same file still gets a new row
        // and the old one is still deleted - which is what keeps replacement one
        // code path rather than two.
        first.ContentHash.Should().Be(second.ContentHash);
        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void Bytes_that_are_not_an_image_cannot_become_a_stored_image()
    {
        var act = () => StoredImage.Capture(
            TenantId, ImageOwnerKind.Member, OwnerId,
            Encoding.UTF8.GetBytes("not a picture"), ActorId, Now);

        act.Should().Throw<ArgumentException>();
    }
}

public sealed class MemberPhotoTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static MemberProfile Profile() => MemberProfile.FromRegistration(
        Guid.NewGuid(), Guid.NewGuid(), "Ravi Shah", "ravi@example.com", Now);

    [Fact]
    public void A_new_profile_has_no_photo()
    {
        Profile().PhotoImageId.Should().BeNull();
    }

    [Fact]
    public void Setting_the_first_photo_reports_nothing_to_delete()
    {
        var profile = Profile();
        var imageId = Guid.NewGuid();

        var replaced = profile.SetPhoto(imageId, Now, Guid.NewGuid());

        replaced.Should().BeNull();
        profile.PhotoImageId.Should().Be(imageId);
    }

    /// <summary>
    /// The aggregate does not know about the images table, so replacing has to
    /// hand back what it stopped pointing at. A method that did not would leave
    /// a photograph of somebody in the database with nothing referring to it and
    /// no path that would ever find it again.
    /// </summary>
    [Fact]
    public void Replacing_a_photo_reports_the_one_it_replaced()
    {
        var profile = Profile();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        profile.SetPhoto(first, Now, Guid.NewGuid());
        var replaced = profile.SetPhoto(second, Now, Guid.NewGuid());

        replaced.Should().Be(first);
        profile.PhotoImageId.Should().Be(second);
    }

    [Fact]
    public void Removing_a_photo_reports_the_one_to_delete()
    {
        var profile = Profile();
        var imageId = Guid.NewGuid();
        profile.SetPhoto(imageId, Now, Guid.NewGuid());

        var removed = profile.RemovePhoto(Now, Guid.NewGuid());

        removed.Should().Be(imageId);
        profile.PhotoImageId.Should().BeNull();
    }

    /// <summary>
    /// Removing a photo that is not there is success and changes nothing - a
    /// member clicking twice, or a client retrying, has done nothing wrong. It
    /// also raises no event, because nothing happened.
    /// </summary>
    [Fact]
    public void Removing_a_photo_that_is_not_there_changes_nothing()
    {
        var profile = Profile();
        profile.ClearDomainEvents();

        var removed = profile.RemovePhoto(Now, Guid.NewGuid());

        removed.Should().BeNull();
        profile.PhotoImageId.Should().BeNull();
        profile.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// Names, never values - the same rule <c>Update</c> follows. The audit
    /// table is append-only and deliberately hard to redact, so an image id
    /// travelling on the event would put a pointer to a photograph of somebody
    /// somewhere it cannot be taken out of.
    /// </summary>
    [Fact]
    public void The_event_says_the_photo_changed_and_never_which_image()
    {
        var profile = Profile();
        profile.ClearDomainEvents();
        var imageId = Guid.NewGuid();

        profile.SetPhoto(imageId, Now, Guid.NewGuid());

        var raised = profile.DomainEvents.Should().ContainSingle().Subject;
        raised.Should().BeOfType<MemberProfileUpdatedDomainEvent>();

        var updated = (MemberProfileUpdatedDomainEvent)raised;
        updated.ChangedFields.Should().ContainSingle().Which.Should().Be("PhotoImageId");
        updated.ToString().Should().NotContain(imageId.ToString());
    }

    /// <summary>
    /// Erasure clears the reference. That it also deletes the bytes is the
    /// handler's job and is asserted in the integration suite - here the point
    /// is that the profile stops naming an image at all.
    /// </summary>
    [Fact]
    public void Erasure_leaves_no_photo_on_the_profile()
    {
        var profile = Profile();
        profile.SetPhoto(Guid.NewGuid(), Now, Guid.NewGuid());

        profile.Erase(Now);

        profile.PhotoImageId.Should().BeNull();
    }
}
