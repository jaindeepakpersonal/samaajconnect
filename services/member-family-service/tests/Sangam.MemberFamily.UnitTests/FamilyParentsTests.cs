using FluentAssertions;
using Sangam.MemberFamily.Domain.Families;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// Who a member's parents are, for the directory card. Every relationship is
/// recorded relative to the family admin, so parents are knowable for the
/// admin, a sibling and a child - and for nobody else.
/// </summary>
public sealed class FamilyParentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Spouse = Guid.NewGuid();
    private static readonly Guid Father = Guid.NewGuid();
    private static readonly Guid Mother = Guid.NewGuid();
    private static readonly Guid Brother = Guid.NewGuid();
    private static readonly Guid Son = Guid.NewGuid();

    /// <summary>An admin with a spouse, two parents, a brother and a son, all accepted.</summary>
    private static Family Household()
    {
        var family = Family.Create(Guid.NewGuid(), Admin, "ABCD2345", Now);

        foreach (var (who, relationship) in new[]
        {
            (Spouse, Relationship.Spouse),
            (Father, Relationship.Parent),
            (Mother, Relationship.Parent),
            (Brother, Relationship.Sibling),
            (Son, Relationship.Child),
        })
        {
            var request = family.RequestJoin(who, relationship, Now)!;
            family.DecideJoinRequest(request.Id, accepted: true, Admin, Now);
        }

        return family;
    }

    [Fact]
    public void The_admins_parents_are_whoever_joined_as_their_parent()
    {
        Household().ParentsOf(Admin).Should().BeEquivalentTo([Father, Mother]);
    }

    [Fact]
    public void A_sibling_shares_the_admins_parents()
    {
        Household().ParentsOf(Brother).Should().BeEquivalentTo([Father, Mother]);
    }

    [Fact]
    public void A_childs_parents_are_the_admin_and_the_admins_spouse()
    {
        Household().ParentsOf(Son).Should().BeEquivalentTo([Admin, Spouse]);
    }

    [Fact]
    public void A_spouse_or_a_parent_has_parents_this_household_does_not_record()
    {
        // Not a guess: the admin's parents are not the spouse's parents.
        var family = Household();

        family.ParentsOf(Spouse).Should().BeEmpty();
        family.ParentsOf(Father).Should().BeEmpty();
    }

    [Fact]
    public void A_pending_request_is_neither_a_parent_nor_given_any()
    {
        var family = Family.Create(Guid.NewGuid(), Admin, "ABCD2345", Now);
        var waitingParent = Guid.NewGuid();
        var waitingChild = Guid.NewGuid();

        family.RequestJoin(waitingParent, Relationship.Parent, Now);
        family.RequestJoin(waitingChild, Relationship.Child, Now);

        family.ParentsOf(Admin).Should().BeEmpty("a parent nobody has accepted is not in the household yet");
        family.ParentsOf(waitingChild).Should().BeEmpty();
    }

    [Fact]
    public void Somebody_outside_the_household_has_none()
    {
        Household().ParentsOf(Guid.NewGuid()).Should().BeEmpty();
    }
}
