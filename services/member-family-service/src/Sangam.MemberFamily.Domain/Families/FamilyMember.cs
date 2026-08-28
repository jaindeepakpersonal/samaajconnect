namespace Sangam.MemberFamily.Domain.Families;

public sealed class FamilyMember
{
    public Guid Id { get; private set; }
    public Guid FamilyId { get; private set; }
    public Guid MemberProfileId { get; private set; }
    public Relationship Relationship { get; private set; }
    public FamilyMemberStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    private FamilyMember() { }

    internal FamilyMember(
        Guid familyId,
        Guid memberProfileId,
        Relationship relationship,
        FamilyMemberStatus status,
        DateTimeOffset requestedAt)
    {
        Id = Guid.NewGuid();
        FamilyId = familyId;
        MemberProfileId = memberProfileId;
        Relationship = relationship;
        Status = status;
        RequestedAt = requestedAt;
    }

    internal void Decide(bool accepted, Guid decidedBy, DateTimeOffset decidedAt)
    {
        Status = accepted ? FamilyMemberStatus.Active : FamilyMemberStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
    }
}
