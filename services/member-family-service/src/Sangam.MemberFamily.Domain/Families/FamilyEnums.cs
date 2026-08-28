namespace Sangam.MemberFamily.Domain.Families;

public enum Relationship
{
    Spouse = 1,
    Parent = 2,
    Sibling = 3,
    Child = 4,
    Other = 5,
}

public enum FamilyMemberStatus
{
    PendingJoinRequest = 1,
    Active = 2,
    Rejected = 3,
}
