using System.Security.Cryptography;
using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Families;

/// <summary>
/// A household within one Samaaj. Other members join it by quoting its
/// <see cref="FamilyCode"/>, which the head then accepts or rejects.
/// </summary>
public sealed class Family : AggregateRoot, ITenantScopedEntity
{
    /// <summary>
    /// Excludes 0/O and 1/I/L, which are the characters people misread when a
    /// code is spoken aloud or written down - which is how a family code
    /// actually travels between relatives.
    /// </summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private const int CodeLength = 8;

    private readonly List<FamilyMember> _members = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FamilyHeadMemberId { get; private set; }

    /// <summary>Unique per Samaaj, not platform-wide: two Samaaj may share a code without confusion.</summary>
    public string FamilyCode { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<FamilyMember> Members => _members.AsReadOnly();

    private Family() { }

    public static Family Create(Guid tenantId, Guid headMemberId, string familyCode, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyCode);

        var family = new Family
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FamilyHeadMemberId = headMemberId,
            FamilyCode = familyCode.Trim().ToUpperInvariant(),
            CreatedAt = createdAt,
        };

        // The head is a member of their own family from the start, and needs no
        // approval to be one.
        family._members.Add(new FamilyMember(
            family.Id, headMemberId, Relationship.Other, FamilyMemberStatus.Active, createdAt));

        family.Raise(new FamilyCreatedDomainEvent(family.Id, tenantId, headMemberId, createdAt));

        return family;
    }

    /// <summary>Generates a code that is readable aloud. Uniqueness is checked by the caller.</summary>
    public static string GenerateCode() =>
        string.Concat(
            Enumerable.Range(0, CodeLength)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));

    public FamilyMember? FindMember(Guid memberProfileId) =>
        _members.FirstOrDefault(m => m.MemberProfileId == memberProfileId);

    public bool IsHead(Guid memberProfileId) => FamilyHeadMemberId == memberProfileId;

    /// <summary>
    /// Records a request to join. Returns null when this member already has a
    /// standing request or membership, so a repeated click is a no-op rather
    /// than a second row for the head to decide twice.
    /// </summary>
    public FamilyMember? RequestJoin(Guid memberProfileId, Relationship relationship, DateTimeOffset requestedAt)
    {
        var existing = FindMember(memberProfileId);

        if (existing is not null && existing.Status != FamilyMemberStatus.Rejected)
        {
            return null;
        }

        // A previously rejected request may be made again - circumstances and
        // minds both change - so the old row is replaced rather than reused.
        if (existing is not null)
        {
            _members.Remove(existing);
        }

        var request = new FamilyMember(
            Id, memberProfileId, relationship, FamilyMemberStatus.PendingJoinRequest, requestedAt);

        _members.Add(request);

        return request;
    }

    public bool DecideJoinRequest(Guid requestId, bool accepted, Guid decidedBy, DateTimeOffset decidedAt)
    {
        var request = _members.FirstOrDefault(
            m => m.Id == requestId && m.Status == FamilyMemberStatus.PendingJoinRequest);

        if (request is null)
        {
            return false;
        }

        request.Decide(accepted, decidedBy, decidedAt);

        return true;
    }
}
