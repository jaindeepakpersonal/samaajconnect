using FluentAssertions;
using Sangam.Matrimony.Domain.Biodata;
using Sangam.Matrimony.Domain.Biodata.Events;
using Xunit;

namespace Sangam.Matrimony.UnitTests;

public sealed class BiodataProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static BiodataFields Fields(DateOnly? dateOfBirth = null) => new(
        "Ravi Shah",
        Gender.Male,
        dateOfBirth ?? new DateOnly(1995, 4, 2),
        GautraId: Guid.NewGuid(),
        ProfessionId: Guid.NewGuid(),
        QualificationId: Guid.NewGuid(),
        CityId: Guid.NewGuid(),
        LocalityId: Guid.NewGuid(),
        HeightCm: 175,
        MaritalStatusId: Guid.NewGuid(),
        Diet: Domain.Biodata.Diet.JainVegetarian,
        FamilyType: Domain.Biodata.FamilyType.Nuclear,
        FatherOccupation: "Business",
        MotherOccupation: "Homemaker",
        SiblingsCount: 1,
        Manglik: false,
        HoroscopeNotes: null,
        AboutMe: "Loves cricket.",
        PreferredAgeMin: 24,
        PreferredAgeMax: 30,
        PartnerPreferenceNotes: null);

    [Fact]
    public void Opting_in_requires_the_members_own_consent()
    {
        var act = () => BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: false, "2026-09-12.1", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Opting_in_requires_a_notice_version()
    {
        var act = () => BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: true, "", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_member_under_18_cannot_opt_in()
    {
        var seventeenYearsAgo = DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-17);

        var act = () => BiodataProfile.OptIn(
            MemberId, TenantId, Fields(seventeenYearsAgo), consentGiven: true, "2026-09-12.1", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Opting_in_creates_an_active_profile_and_raises_an_event()
    {
        var profile = BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: true, "2026-09-12.1", Now);

        profile.Id.Should().Be(MemberId);
        profile.IsActive.Should().BeTrue();
        profile.Consent.Stands.Should().BeTrue();
        profile.Consent.NoticeVersion.Should().Be("2026-09-12.1");
        profile.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BiodataProfileCreatedDomainEvent>();
    }

    [Fact]
    public void Withdrawing_deactivates_but_keeps_every_field()
    {
        var profile = BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: true, "2026-09-12.1", Now);
        profile.ClearDomainEvents();

        profile.WithdrawConsent(Now.AddDays(1));

        profile.IsActive.Should().BeFalse();
        profile.Consent.Stands.Should().BeFalse();
        // The point of withdrawal-not-erasure: the fields are untouched.
        profile.FullName.Should().Be("Ravi Shah");
        profile.AboutMe.Should().Be("Loves cricket.");
        profile.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<MatrimonyConsentWithdrawnDomainEvent>();
    }

    [Fact]
    public void Withdrawing_twice_is_a_no_op()
    {
        var profile = BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: true, "2026-09-12.1", Now);
        profile.WithdrawConsent(Now.AddDays(1));
        profile.ClearDomainEvents();

        profile.WithdrawConsent(Now.AddDays(2));

        profile.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Rejoining_after_withdrawal_reactivates_with_a_fresh_consent()
    {
        var profile = BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: true, "2026-09-12.1", Now);
        profile.WithdrawConsent(Now.AddDays(1));

        profile.Rejoin(Fields(), Now.AddDays(2));

        profile.IsActive.Should().BeTrue();
        profile.Consent.Stands.Should().BeTrue();
        profile.Consent.GivenAt.Should().Be(Now.AddDays(2));
    }

    [Fact]
    public void Erasing_clears_every_field_a_member_could_be_recognised_by()
    {
        var profile = BiodataProfile.OptIn(MemberId, TenantId, Fields(), consentGiven: true, "2026-09-12.1", Now);

        profile.Erase(Now.AddDays(1));

        profile.FullName.Should().Be("Erased member");
        profile.GautraId.Should().BeNull();
        profile.AboutMe.Should().BeNull();
        profile.FatherOccupation.Should().BeNull();
        profile.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Age_is_computed_from_date_of_birth_not_stored()
    {
        var dateOfBirth = new DateOnly(2000, 6, 15);

        BiodataProfile.AgeInYearsOn(dateOfBirth, new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero))
            .Should().Be(25);
        BiodataProfile.AgeInYearsOn(dateOfBirth, new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero))
            .Should().Be(26);
    }
}
