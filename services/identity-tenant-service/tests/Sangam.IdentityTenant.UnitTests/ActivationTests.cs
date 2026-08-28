using FluentAssertions;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Security;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class ActivationCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void An_issued_code_is_returned_once_and_stored_only_as_a_hash()
    {
        var (code, plaintext) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);

        plaintext.Should().NotBeNullOrWhiteSpace();

        // A database copy must not be a set of working codes.
        code.Hash.Should().NotContain(plaintext);
        _hasher.Verify(plaintext, code.Hash).Should().BeTrue();
    }

    [Fact]
    public void A_code_avoids_the_characters_people_misread()
    {
        // These are read aloud or written on paper, which is exactly when
        // 0/O and 1/I/L go wrong.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var (_, plaintext) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);

            plaintext.Should().MatchRegex("^[A-HJ-NP-Z2-9]{10}$");
        }
    }

    [Fact]
    public void Two_issues_produce_different_codes()
    {
        var (_, first) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);
        var (_, second) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);

        first.Should().NotBe(second);
    }

    [Fact]
    public void A_fresh_code_is_usable_and_expires_after_a_week()
    {
        var (code, _) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);

        code.IsUsable(Now).Should().BeTrue();
        code.IsUsable(Now.Add(ActivationCode.Lifetime).AddSeconds(-1)).Should().BeTrue();
        code.IsUsable(Now.Add(ActivationCode.Lifetime)).Should().BeFalse();
    }

    [Fact]
    public void Five_wrong_guesses_kill_the_code()
    {
        var (code, _) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);

        for (var attempt = 0; attempt < ActivationCode.MaxAttempts; attempt++)
        {
            code.RecordFailedAttempt();
        }

        code.IsUsable(Now).Should().BeFalse();
    }

    [Fact]
    public void Four_wrong_guesses_do_not()
    {
        var (code, _) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);

        for (var attempt = 0; attempt < ActivationCode.MaxAttempts - 1; attempt++)
        {
            code.RecordFailedAttempt();
        }

        code.IsUsable(Now).Should().BeTrue();
    }
}

public sealed class UserActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();

    private readonly PasswordHasher _hasher = new();

    private static User Converted() => User.CreateFromChildConversion(
        TenantId,
        " Aarav@Example.COM ",
        " Aarav Jain ",
        ChildId,
        AuthorizationCatalog.RoleIds.Member,
        Now);

    [Fact]
    public void A_converted_child_account_starts_with_no_password_and_cannot_be_signed_into()
    {
        var user = Converted();

        user.Status.Should().Be(UserStatus.PendingActivation);
        user.PasswordHash.Should().BeEmpty();
        user.IsContactVerified.Should().BeFalse();
    }

    [Fact]
    public void It_normalises_the_identifier_and_name_like_any_other_account()
    {
        var user = Converted();

        user.MobileOrEmail.Should().Be("aarav@example.com");
        user.FullName.Should().Be("Aarav Jain");
    }

    [Fact]
    public void It_remembers_which_child_record_it_came_from()
    {
        Converted().ConvertedFromChildProfileId.Should().Be(ChildId);
    }

    [Fact]
    public void It_gets_the_Member_role_in_its_own_Samaaj()
    {
        var role = Converted().Roles.Should().ContainSingle().Subject;

        role.RoleId.Should().Be(AuthorizationCatalog.RoleIds.Member);
        role.TenantScope.Should().Be(TenantId);
    }

    [Fact]
    public void Activating_sets_the_password_opens_the_account_and_announces_it()
    {
        var user = Converted();
        var (code, _) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);
        user.AttachActivationCode(code);

        user.Activate(_hasher.Hash("a-long-enough-password"), Now.AddDays(1));

        user.Status.Should().Be(UserStatus.Active);
        _hasher.Verify("a-long-enough-password", user.PasswordHash).Should().BeTrue();

        var raised = user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserActivatedFromChildDomainEvent>().Subject;

        raised.ChildProfileId.Should().Be(ChildId);
        raised.Topic.Should().Be("identity.child-conversion.completed.v1");
    }

    [Fact]
    public void Activating_counts_as_verifying_the_contact_detail()
    {
        var user = Converted();
        user.AttachActivationCode(ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now).Code);

        user.Activate(_hasher.Hash("a-long-enough-password"), Now);

        // Redeeming a code proves the person holds the contact detail an admin
        // wrote down for them - the same assurance OTP would give.
        user.IsContactVerified.Should().BeTrue();
    }

    [Fact]
    public void The_code_is_spent_by_activation()
    {
        var user = Converted();
        user.AttachActivationCode(ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now).Code);

        user.Activate(_hasher.Hash("a-long-enough-password"), Now);

        // A code that still worked afterwards would be a second way in.
        user.ActivationCode.Should().BeNull();
    }

    [Fact]
    public void Re_issuing_replaces_the_previous_code()
    {
        var user = Converted();

        var (first, _) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now);
        user.AttachActivationCode(first);

        var (second, _) = ActivationCode.Issue(Guid.NewGuid(), _hasher.Hash, Now.AddDays(1));
        user.AttachActivationCode(second);

        // Codes expire and paper gets lost, so re-issuing is expected - but the
        // old one must stop working.
        user.ActivationCode.Should().BeSameAs(second);
    }
}
