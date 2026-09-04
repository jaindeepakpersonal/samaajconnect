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

    /// <summary>
    /// The outcome of a member trying to take back their own join request.
    /// </summary>
    public enum WithdrawOutcome
    {
        /// <summary>The pending request is gone.</summary>
        Withdrawn,

        /// <summary>There was nothing pending. The end state is the same.</summary>
        NothingPending,

        /// <summary>
        /// The head accepted between the member asking and withdrawing, so this
        /// is now a membership rather than a request.
        /// </summary>
        AlreadyAccepted,
    }

    /// <summary>
    /// Takes back a request this member made and nobody has decided.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Without this a member could be stuck forever.</b> A pending request
    /// counts as belonging to a household, deliberately - otherwise somebody
    /// could ask two families at once and both heads could accept. But nothing
    /// could cancel one, so a request the head never got round to deciding left
    /// that member unable to join any other household or create their own, with
    /// no way out that did not involve someone else acting. That is not an
    /// erasure edge case; it is what happens when a head is simply slow.
    /// </para>
    /// <para>
    /// <b>Only a pending request, never a membership.</b> Leaving a household
    /// you are actually in is a different act with different consequences - the
    /// children in it are held on somebody's consent, and the head's departure
    /// is what <see cref="SucceedHeadAfterRemoval"/> exists for. Collapsing the
    /// two into one call would let "cancel my request" quietly mean "leave my
    /// family" for anyone whose request had just been accepted, which is why
    /// that case is refused by name rather than treated as success.
    /// </para>
    /// </remarks>
    public WithdrawOutcome WithdrawJoinRequest(Guid memberProfileId)
    {
        var existing = FindMember(memberProfileId);

        if (existing is null || existing.Status == FamilyMemberStatus.Rejected)
        {
            return WithdrawOutcome.NothingPending;
        }

        if (existing.Status != FamilyMemberStatus.PendingJoinRequest)
        {
            return WithdrawOutcome.AlreadyAccepted;
        }

        _members.Remove(existing);

        return WithdrawOutcome.Withdrawn;
    }

    /// <summary>
    /// Active members, oldest membership first.
    /// </summary>
    /// <remarks>
    /// Pending and rejected rows are not members. Somebody who has only asked to
    /// join is not somebody a household can be handed to, and not somebody whose
    /// presence should stop the last real member leaving.
    /// </remarks>
    public IReadOnlyList<FamilyMember> ActiveMembers() =>
        [.. _members
            .Where(m => m.Status == FamilyMemberStatus.Active)
            .OrderBy(m => m.DecidedAt ?? m.RequestedAt)
            .ThenBy(m => m.Id)];

    /// <summary>
    /// Hands headship to the longest-standing remaining member, and answers who
    /// that is — or null when the household has nobody left to head it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called when the head's own membership row has just been removed, which
    /// happens when they erase their account. Until this existed the household
    /// kept the erased member's id as its head, so <see cref="IsHead"/> was
    /// false for everybody and four things stopped working at once: deciding a
    /// join request, adding a child, starting a conversion, and seeing the
    /// family code to invite anyone. A household of five people was frozen
    /// because one of them exercised a right.
    /// </para>
    /// <para>
    /// <b>This is a deliberate change of mind.</b> The erasure consumer used to
    /// say re-heading "belongs in an admin command, not here". An admin command
    /// needs an administrator to notice, and nothing tells them - so the
    /// household stays broken until somebody complains, which is a worse
    /// failure than the consumer making a small, explicable decision. Succeeding
    /// at the moment of erasure also means the headless state never exists,
    /// rather than existing until repaired.
    /// </para>
    /// <para>
    /// Longest-standing rather than any other rule, because it is the one that
    /// needs no judgement and can be explained to the household in a sentence.
    /// It is the earliest to have joined, not the earliest to have asked: a
    /// request that was accepted last week does not outrank a member of ten
    /// years because they filled a form first.
    /// </para>
    /// </remarks>
    public Guid? SucceedHeadAfterRemoval(Guid removedMemberId)
    {
        if (FamilyHeadMemberId != removedMemberId)
        {
            return null;
        }

        var successor = _members
            .Where(m => m.Status == FamilyMemberStatus.Active)
            .OrderBy(m => m.DecidedAt ?? m.RequestedAt)
            .ThenBy(m => m.Id)
            .FirstOrDefault();

        if (successor is null)
        {
            // Nobody left. The household keeps the departed id as its head and
            // is inert: no member can see it and no request can be made to it,
            // because the code is only ever shown to a head. Leaving the field
            // alone is better than inventing a head who is not there.
            return null;
        }

        FamilyHeadMemberId = successor.MemberProfileId;

        return FamilyHeadMemberId;
    }

    /// <summary>
    /// Removes one member's link to this household. Returns false when they
    /// were never in it, which is a normal outcome under at-least-once
    /// delivery rather than an error.
    /// </summary>
    public bool RemoveMember(Guid memberProfileId)
    {
        var existing = FindMember(memberProfileId);

        if (existing is null)
        {
            return false;
        }

        _members.Remove(existing);

        return true;
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
