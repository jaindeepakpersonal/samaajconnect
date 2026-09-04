using FluentAssertions;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// What a Samaaj administrator may change about somebody else, and what they
/// may not.
/// </summary>
/// <remarks>
/// The distinction is not a nicety. `Members.Write` has always been granted to
/// SamaajAdmin and `UpdateProfileCommand` has always accepted them - but that
/// command replaces the profile whole and therefore requires the member's
/// privacy levels, which no read available to an administrator returns. The
/// only outcomes were guessing, or sending nothing and having every level parse
/// as Private. `CorrectDetails` removes the question by not carrying the
/// fields.
/// </remarks>
public sealed class AdminCorrectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static MemberProfile Member()
    {
        var profile = MemberProfile.FromRegistration(
            Guid.NewGuid(), Guid.NewGuid(), "Ravi Shar", "ravi@example.com", Now);

        // What this member decided for themselves: the mobile is shared with
        // the Samaaj, the address is not, and they are in the directory.
        profile.Update(
            "Ravi Shar",
            dateOfBirth: null,
            gender: Gender.Male,
            mobile: "+919812345678",
            email: "ravi@example.com",
            address: "12 Old Road",
            locality: "Udaipur",
            profession: "Teacher",
            new FieldPrivacy(
                Mobile: PrivacyLevel.Public,
                Email: PrivacyLevel.Public,
                Address: PrivacyLevel.Private,
                Profession: PrivacyLevel.Public,
                DateOfBirth: PrivacyLevel.Private),
            isListedInDirectory: true,
            Now,
            profile.Id);

        return profile;
    }

    [Fact]
    public void An_administrator_can_fix_a_misspelt_name()
    {
        var profile = Member();

        profile.CorrectDetails(
            "Ravi Shah",
            profile.DateOfBirth,
            profile.Gender,
            profile.Mobile,
            profile.Email,
            profile.Address,
            profile.Locality,
            profile.Profession,
            Now,
            Guid.NewGuid());

        profile.FullName.Should().Be("Ravi Shah");
    }

    [Fact]
    public void And_what_the_member_shares_is_untouched_by_it()
    {
        var profile = Member();
        var before = profile.Privacy;

        profile.CorrectDetails(
            "Ravi Shah",
            profile.DateOfBirth,
            profile.Gender,
            "+919800000000",
            profile.Email,
            profile.Address,
            profile.Locality,
            profile.Profession,
            Now,
            Guid.NewGuid());

        // The point of the whole command. A correction that reset these would
        // be indistinguishable, from the member's side, from the platform
        // deciding to publish their address.
        profile.Privacy.Should().Be(before);
        profile.Privacy.Address.Should().Be(PrivacyLevel.Private);
        profile.Privacy.Mobile.Should().Be(PrivacyLevel.Public);
        profile.IsListedInDirectory.Should().BeTrue();
    }

    [Fact]
    public void A_member_who_is_unlisted_stays_unlisted_through_a_correction()
    {
        var profile = Member();

        profile.Update(
            profile.FullName,
            profile.DateOfBirth,
            profile.Gender,
            profile.Mobile,
            profile.Email,
            profile.Address,
            profile.Locality,
            profile.Profession,
            profile.Privacy,
            isListedInDirectory: false,
            Now,
            profile.Id);

        profile.CorrectDetails(
            "Ravi Shah",
            profile.DateOfBirth,
            profile.Gender,
            profile.Mobile,
            profile.Email,
            profile.Address,
            profile.Locality,
            profile.Profession,
            Now,
            Guid.NewGuid());

        // Taking yourself out of the directory is the setting most likely to be
        // undone by accident, because the value that restores it is the
        // default. A correction must not be what puts somebody back.
        profile.IsListedInDirectory.Should().BeFalse();
    }

    [Fact]
    public void The_correction_records_who_made_it_and_which_fields_moved()
    {
        var profile = Member();
        var admin = Guid.NewGuid();

        profile.CorrectDetails(
            "Ravi Shah",
            profile.DateOfBirth,
            profile.Gender,
            "+919800000000",
            profile.Email,
            profile.Address,
            profile.Locality,
            profile.Profession,
            Now,
            admin);

        var raised = profile.DomainEvents
            .OfType<MemberProfileUpdatedDomainEvent>()
            .Last();

        raised.UpdatedBy.Should().Be(admin);

        // Names, never values: SECURITY-CHECKLIST.md asks what an administrator
        // touched, and the audit service stores payloads verbatim.
        raised.ChangedFields.Should().BeEquivalentTo(["FullName", "Mobile"]);
    }

    [Fact]
    public void A_correction_that_changes_nothing_says_so()
    {
        var profile = Member();

        profile.CorrectDetails(
            profile.FullName,
            profile.DateOfBirth,
            profile.Gender,
            profile.Mobile,
            profile.Email,
            profile.Address,
            profile.Locality,
            profile.Profession,
            Now,
            Guid.NewGuid());

        profile.DomainEvents
            .OfType<MemberProfileUpdatedDomainEvent>()
            .Last()
            .ChangedFields.Should().BeEmpty();
    }
}
