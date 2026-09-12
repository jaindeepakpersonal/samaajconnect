using Sangam.Matrimony.Domain.Common;
using Sangam.Matrimony.Domain.Proposals.Events;

namespace Sangam.Matrimony.Domain.Proposals;

/// <summary>
/// A marriage proposal from one member's biodata to another's. Its own
/// aggregate root, not owned by either <see cref="Biodata.BiodataProfile"/> -
/// a proposal is a peer-to-peer fact between two independent aggregates, the
/// same reasoning that keeps a Boli's bid history its own record rather than
/// nested under one bidder.
/// </summary>
public sealed class Proposal : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FromMemberId { get; private set; }
    public Guid ToMemberId { get; private set; }
    public ProposalStatus Status { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    private Proposal() { }

    public static Proposal Send(
        Guid tenantId, Guid fromMemberId, Guid toMemberId, string? message, DateTimeOffset sentAt)
    {
        if (fromMemberId == toMemberId)
        {
            throw new ArgumentException("A member cannot send a proposal to themselves.", nameof(toMemberId));
        }

        var proposal = new Proposal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FromMemberId = fromMemberId,
            ToMemberId = toMemberId,
            Status = ProposalStatus.Pending,
            Message = Normalize(message),
            SentAt = sentAt,
        };

        proposal.Raise(new ProposalSentDomainEvent(proposal.Id, tenantId, fromMemberId, toMemberId, sentAt));

        return proposal;
    }

    /// <summary>
    /// The recipient decides. Refused from any state but <c>Pending</c> -
    /// enforced here as well as in the handler, so a future caller cannot
    /// bypass it by forgetting to check first.
    /// </summary>
    public void Respond(bool accept, Guid respondingMemberId, DateTimeOffset respondedAt)
    {
        if (respondingMemberId != ToMemberId)
        {
            throw new InvalidOperationException("Only the recipient of a proposal may respond to it.");
        }

        if (Status != ProposalStatus.Pending)
        {
            throw new InvalidOperationException("This proposal has already been decided.");
        }

        Status = accept ? ProposalStatus.Accepted : ProposalStatus.Declined;
        RespondedAt = respondedAt;

        Raise(new ProposalRespondedDomainEvent(Id, TenantId, FromMemberId, ToMemberId, Status.ToString(), respondedAt));
    }

    /// <summary>
    /// The sender takes it back before it is decided - mirrors
    /// member-family-service's `WithdrawJoinRequestCommand`: a member could
    /// ask and never take it back was a real, previously-hit bug shape on
    /// this platform, so the sender's own way out is built in from the
    /// start here.
    /// </summary>
    public void Withdraw(Guid withdrawingMemberId, DateTimeOffset withdrawnAt)
    {
        if (withdrawingMemberId != FromMemberId)
        {
            throw new InvalidOperationException("Only the sender of a proposal may withdraw it.");
        }

        if (Status != ProposalStatus.Pending)
        {
            throw new InvalidOperationException("This proposal has already been decided.");
        }

        Status = ProposalStatus.Withdrawn;
        RespondedAt = withdrawnAt;

        Raise(new ProposalWithdrawnDomainEvent(Id, TenantId, FromMemberId, ToMemberId, withdrawnAt));
    }

    /// <summary>
    /// Strips anything that could re-identify an erased member from a
    /// proposal they were part of, keeping the row so the other party's own
    /// sent/received history is not silently broken - the same "keep the
    /// row, strip the person" shape member-family-service's own
    /// `ChildProfile` erasure already uses.
    /// </summary>
    public void DeIdentify(DateTimeOffset now)
    {
        Message = null;

        if (Status == ProposalStatus.Pending)
        {
            Status = ProposalStatus.Withdrawn;
            RespondedAt = now;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
