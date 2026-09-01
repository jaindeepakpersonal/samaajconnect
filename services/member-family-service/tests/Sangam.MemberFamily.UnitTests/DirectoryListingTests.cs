using FluentAssertions;
using Sangam.MemberFamily.Application.Members.Commands.UpdateProfile;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// Being unlisted, which per-field privacy could not express: a member who marks
/// every field Private is still in the directory under their name, because a
/// directory listing is a name.
/// </summary>
public sealed class DirectoryListingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static MemberProfile Profile() =>
        MemberProfile.FromRegistration(
            Guid.NewGuid(), Guid.NewGuid(), "Ravi Shah", "ravi@example.com", Now);

    private static void Update(MemberProfile profile, bool listed) =>
        profile.Update(
            "Ravi Shah",
            photoUrl: null,
            dateOfBirth: null,
            gender: Gender.Male,
            mobile: "+919812345678",
            email: "ravi@example.com",
            address: null,
            locality: "Udaipur",
            profession: null,
            FieldPrivacy.Default,
            isListedInDirectory: listed,
            Now,
            Guid.NewGuid());

    [Fact]
    public void A_new_member_is_in_the_directory()
    {
        // A directory nobody is in by default is not a directory, and the
        // wireframe draws the checkbox ticked.
        Profile().IsListedInDirectory.Should().BeTrue();
    }

    [Fact]
    public void A_member_can_take_themselves_out_of_it()
    {
        var profile = Profile();

        Update(profile, listed: false);

        profile.IsListedInDirectory.Should().BeFalse();
    }

    [Fact]
    public void And_put_themselves_back()
    {
        var profile = Profile();

        Update(profile, listed: false);
        Update(profile, listed: true);

        profile.IsListedInDirectory.Should().BeTrue();
    }

    [Fact]
    public void Changing_it_is_recorded_on_the_event_like_any_other_field()
    {
        // A Samaaj admin can edit a member's profile, so "who took this member
        // out of the directory" has to be answerable from the audit trail.
        var profile = Profile();
        profile.ClearDomainEvents();

        Update(profile, listed: false);

        profile.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<MemberProfileUpdatedDomainEvent>()
            .Which.ChangedFields.Should().Contain(nameof(MemberProfile.IsListedInDirectory));
    }

    [Fact]
    public void Leaving_it_alone_does_not_report_a_change()
    {
        var profile = Profile();
        profile.ClearDomainEvents();

        Update(profile, listed: true);

        profile.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<MemberProfileUpdatedDomainEvent>()
            .Which.ChangedFields.Should().NotContain(nameof(MemberProfile.IsListedInDirectory));
    }

    [Fact]
    public void Erasing_a_member_takes_them_out_of_the_directory()
    {
        // The row survives so family links do not dangle. "Erased member" has no
        // business appearing in a list of people you can look up, and before
        // this flag existed there was no way to say so.
        var profile = Profile();

        profile.Erase(Now);

        profile.IsListedInDirectory.Should().BeFalse();
    }
}

public sealed class UpdateProfileCommandValidatorDirectoryTests
{
    private readonly UpdateProfileCommandValidator _validator = new();

    private static UpdateProfileCommand Command(bool? listed) => new(
        Guid.NewGuid(),
        "Ravi Shah",
        PhotoUrl: null,
        DateOfBirth: null,
        Gender: "Male",
        Mobile: null,
        Email: null,
        Address: null,
        Locality: null,
        Profession: null,
        new PrivacySettings("SamaajOnly", "Private", "Private", "SamaajOnly", "Private"),
        listed);

    [Fact]
    public void Omitting_the_directory_setting_is_refused()
    {
        // Not defaulted to true. This command replaces the whole profile, so a
        // body without it is malformed - and defaulting would put a member who
        // had taken themselves out of the directory back into it, silently,
        // because they edited their address.
        var result = _validator.Validate(Command(listed: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateProfileCommand.IsListedInDirectory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Either_answer_is_accepted(bool listed) =>
        _validator.Validate(Command(listed)).IsValid.Should().BeTrue();
}
