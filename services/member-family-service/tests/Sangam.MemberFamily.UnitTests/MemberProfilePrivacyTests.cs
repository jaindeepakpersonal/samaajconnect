using FluentAssertions;
using Sangam.MemberFamily.Application.Members;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// SECURITY-CHECKLIST.md requires the directory to respect PrivacyLevel per
/// field rather than as one visibility toggle, so each field is checked on its
/// own here.
/// </summary>
public sealed class MemberProfilePrivacyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static MemberProfile ProfileWith(FieldPrivacy privacy)
    {
        var profile = MemberProfile.FromRegistration(
            MemberId, TenantId, "Ravi Shah", "ravi@example.com", Now);

        profile.Update(
            "Ravi Shah",
            photoUrl: null,
            dateOfBirth: new DateOnly(1990, 5, 14),
            gender: Gender.Male,
            mobile: "+919812345678",
            email: "ravi@example.com",
            address: "Udaipur, Rajasthan",
            locality: "Udaipur",
            profession: "Architect",
            privacy,
            Now);

        return profile;
    }

    private static FieldPrivacy AllPrivate => new(
        PrivacyLevel.Private, PrivacyLevel.Private, PrivacyLevel.Private,
        PrivacyLevel.Private, PrivacyLevel.Private);

    private static FieldPrivacy AllSamaajOnly => new(
        PrivacyLevel.SamaajOnly, PrivacyLevel.SamaajOnly, PrivacyLevel.SamaajOnly,
        PrivacyLevel.SamaajOnly, PrivacyLevel.SamaajOnly);

    private static ProfileViewer AnotherMember => new(Guid.NewGuid(), IsSamaajAdmin: false);

    private static ProfileViewer Admin => new(Guid.NewGuid(), IsSamaajAdmin: true);

    private static ProfileViewer Self => new(MemberId, IsSamaajAdmin: false);

    [Fact]
    public void Another_member_sees_the_name_photo_and_locality_that_a_directory_is_for()
    {
        var response = ProfileWith(AllPrivate).ToDirectoryResponse(AnotherMember);

        response.FullName.Should().Be("Ravi Shah");
        response.Locality.Should().Be("Udaipur");
    }

    [Fact]
    public void Another_member_sees_nothing_marked_private()
    {
        var response = ProfileWith(AllPrivate).ToDirectoryResponse(AnotherMember);

        response.Mobile.Should().BeNull();
        response.Email.Should().BeNull();
        response.Address.Should().BeNull();
        response.Profession.Should().BeNull();
        response.DateOfBirth.Should().BeNull();
    }

    [Fact]
    public void Another_member_sees_what_is_shared_with_the_Samaaj()
    {
        var response = ProfileWith(AllSamaajOnly).ToDirectoryResponse(AnotherMember);

        response.Mobile.Should().Be("+919812345678");
        response.Address.Should().Be("Udaipur, Rajasthan");
    }

    [Fact]
    public void Privacy_is_applied_per_field_and_not_as_one_toggle()
    {
        var mixed = new FieldPrivacy(
            Mobile: PrivacyLevel.SamaajOnly,
            Email: PrivacyLevel.Private,
            Address: PrivacyLevel.Private,
            Profession: PrivacyLevel.Public,
            DateOfBirth: PrivacyLevel.Private);

        var response = ProfileWith(mixed).ToDirectoryResponse(AnotherMember);

        response.Mobile.Should().NotBeNull();
        response.Profession.Should().NotBeNull();
        response.Email.Should().BeNull();
        response.Address.Should().BeNull();
        response.DateOfBirth.Should().BeNull();
    }

    [Fact]
    public void A_member_always_sees_their_own_profile_in_full()
    {
        var response = ProfileWith(AllPrivate).ToDirectoryResponse(Self);

        response.Mobile.Should().NotBeNull();
        response.Address.Should().NotBeNull();
    }

    [Fact]
    public void A_Samaaj_admin_sees_every_field_because_correcting_them_is_the_job()
    {
        var response = ProfileWith(AllPrivate).ToDirectoryResponse(Admin);

        response.Email.Should().Be("ravi@example.com");
        response.Address.Should().NotBeNull();
    }

    [Fact]
    public void A_hidden_field_is_null_rather_than_masked()
    {
        // A mask still leaks length and shape.
        var response = ProfileWith(AllPrivate).ToDirectoryResponse(AnotherMember);

        response.Mobile.Should().BeNull();
        response.Mobile.Should().NotBe("+91xxxxxx78");
    }

    [Fact]
    public void A_new_profile_starts_with_contact_details_closed()
    {
        var profile = MemberProfile.FromRegistration(
            MemberId, TenantId, "Ravi Shah", "ravi@example.com", Now);

        // Closed by default and opened deliberately, rather than open by
        // default and hoping the member notices.
        profile.Privacy.Email.Should().Be(PrivacyLevel.Private);
        profile.Privacy.Address.Should().Be(PrivacyLevel.Private);
    }

    [Fact]
    public void Registration_seeds_the_identifier_into_the_matching_contact_field()
    {
        MemberProfile.FromRegistration(MemberId, TenantId, "Ravi", "ravi@example.com", Now)
            .Email.Should().Be("ravi@example.com");

        MemberProfile.FromRegistration(MemberId, TenantId, "Ravi", "9812345678", Now)
            .Mobile.Should().Be("9812345678");
    }

    [Fact]
    public void Updating_a_profile_raises_an_event_for_other_services()
    {
        var profile = ProfileWith(AllSamaajOnly);

        profile.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<MemberProfileUpdatedDomainEvent>();
    }
}
