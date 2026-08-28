using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.IntegrationEvents;
using Sangam.MemberFamily.Application.IntegrationEvents.Commands.EraseMemberData;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

public sealed class ProfileErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static MemberProfile Profile()
    {
        var profile = MemberProfile.FromRegistration(
            Guid.NewGuid(), Guid.NewGuid(), "Ravi Shah", "ravi@example.com", Now);

        profile.Update(
            "Ravi Shah", "https://cdn/photo.jpg", new DateOnly(1985, 4, 2), Gender.Male,
            "9876543210", "ravi@example.com", "12 Temple Road", "Ghatkopar", "Chartered Accountant",
            new FieldPrivacy(
                PrivacyLevel.Public, PrivacyLevel.Public, PrivacyLevel.Public,
                PrivacyLevel.Public, PrivacyLevel.Public),
            Now, Guid.NewGuid());

        return profile;
    }

    [Fact]
    public void Erase_clears_every_field_a_member_supplied()
    {
        var profile = Profile();

        profile.Erase(Now);

        profile.Mobile.Should().BeNull();
        profile.Email.Should().BeNull();
        profile.Address.Should().BeNull();
        profile.Locality.Should().BeNull();
        profile.Profession.Should().BeNull();
        profile.PhotoUrl.Should().BeNull();
        profile.DateOfBirth.Should().BeNull();
        profile.FullName.Should().NotContain("Ravi");
    }

    [Fact]
    public void Erase_closes_the_privacy_settings_as_well_as_the_fields()
    {
        // A profile left Public would keep appearing in the directory as a
        // visible row, which is not what erasure means to the person who asked.
        var profile = Profile();

        profile.Erase(Now);

        profile.Privacy.Mobile.Should().Be(PrivacyLevel.Private);
        profile.Privacy.Email.Should().Be(PrivacyLevel.Private);
        profile.Privacy.Address.Should().Be(PrivacyLevel.Private);
        profile.Privacy.Profession.Should().Be(PrivacyLevel.Private);
        profile.Privacy.DateOfBirth.Should().Be(PrivacyLevel.Private);
    }
}

public sealed class ChildErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static ChildProfile Child() =>
        ChildProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Aarav Shah", new DateOnly(2012, 7, 19),
            Gender.Male, "https://cdn/aarav.jpg", Guid.NewGuid(), Now);

    [Fact]
    public void Erase_clears_the_name_and_photo()
    {
        var child = Child();

        child.Erase();

        child.FullName.Should().NotContain("Aarav");
        child.PhotoUrl.Should().BeNull();
    }

    [Fact]
    public void Erase_keeps_the_birth_year_but_not_the_birthday()
    {
        // Age is what decides eligibility, so the row still has to behave. An
        // exact birthday is how a child would be recognised from it.
        var child = Child();

        child.Erase();

        child.DateOfBirth.Should().Be(new DateOnly(2012, 1, 1));
    }
}

public sealed class EraseMemberDataCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IMemberProfileRepository _profiles = Substitute.For<IMemberProfileRepository>();
    private readonly IFamilyRepository _families = Substitute.For<IFamilyRepository>();
    private readonly IChildRepository _children = Substitute.For<IChildRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly EraseMemberDataCommandHandler _handler;
    private readonly MemberProfile _profile;

    public EraseMemberDataCommandHandlerTests()
    {
        _profile = MemberProfile.FromRegistration(UserId, TenantId, "Ravi Shah", "ravi@example.com", Now);

        _clock.UtcNow.Returns(Now);
        _profiles.GetForConsumerAsync(UserId, Arg.Any<CancellationToken>()).Returns(_profile);

        _handler = new EraseMemberDataCommandHandler(
            _profiles, _families, _children, _unitOfWork, _clock,
            NullLogger<EraseMemberDataCommandHandler>.Instance);
    }

    private static IntegrationEventEnvelope Envelope(string? payload = null) =>
        new(
            Guid.NewGuid(),
            TenantId,
            "identity.user.erased.v1",
            "Sangam.IdentityTenant.Domain.Users.UserErasedDomainEvent",
            payload ?? $$"""{"userId":"{{UserId}}","tenantId":"{{TenantId}}"}""",
            Now.AddMinutes(-1));

    private Task<Application.Common.Result<EraseMemberDataResult>> Handle(string? payload = null) =>
        _handler.Handle(new EraseMemberDataCommand(Envelope(payload)), CancellationToken.None);

    private Family HeadedFamily()
    {
        var family = Family.Create(TenantId, UserId, "ABCD2345", Now);
        _families.GetForConsumerAsync(UserId, Arg.Any<CancellationToken>()).Returns(family);

        return family;
    }

    [Fact]
    public async Task Erases_the_profile()
    {
        var result = await Handle();

        result.Value.Erased.Should().BeTrue();
        _profile.FullName.Should().NotContain("Ravi");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Erases_the_children_this_member_headed()
    {
        var family = HeadedFamily();
        var child = ChildProfile.Create(
            TenantId, family.Id, "Aarav Shah", new DateOnly(2012, 7, 19),
            Gender.Male, null, UserId, Now);

        _children.ListForConsumerAsync(family.Id, Arg.Any<CancellationToken>()).Returns([child]);

        var result = await Handle();

        result.Value.ChildrenErased.Should().Be(1);
        child.FullName.Should().NotContain("Aarav");
    }

    [Fact]
    public async Task Removes_the_household_link_but_leaves_the_household()
    {
        // Other members joined this family. One person exercising their own
        // right must not restructure everyone else's records.
        var family = HeadedFamily();
        var sibling = Guid.NewGuid();
        family.RequestJoin(sibling, Relationship.Sibling, Now);

        await Handle();

        family.FindMember(UserId).Should().BeNull();
        family.FindMember(sibling).Should().NotBeNull();
    }

    [Fact]
    public async Task Leaves_the_children_of_a_family_this_member_only_joined()
    {
        // The parental consent that holds those records is the head's, and the
        // head has not asked for anything.
        var family = Family.Create(TenantId, Guid.NewGuid(), "ABCD2345", Now);
        family.RequestJoin(UserId, Relationship.Sibling, Now);
        _families.GetForConsumerAsync(UserId, Arg.Any<CancellationToken>()).Returns(family);

        var result = await Handle();

        result.Value.ChildrenErased.Should().Be(0);
        await _children.DidNotReceive()
            .ListForConsumerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Is_a_no_op_when_the_profile_is_already_gone()
    {
        // At-least-once delivery makes a repeat normal, not an error.
        _profiles.GetForConsumerAsync(UserId, Arg.Any<CancellationToken>()).Returns((MemberProfile?)null);

        var result = await Handle();

        result.IsSuccess.Should().BeTrue();
        result.Value.Erased.Should().BeFalse();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"userId":"00000000-0000-0000-0000-000000000000"}""")]
    public async Task Erases_nothing_when_the_payload_names_no_one(string payload)
    {
        var result = await Handle(payload);

        result.IsSuccess.Should().BeTrue();
        _profile.FullName.Should().Be("Ravi Shah");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
