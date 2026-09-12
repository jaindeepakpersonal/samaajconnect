using FluentAssertions;
using Sangam.Matrimony.Domain.Proposals;
using Sangam.Matrimony.Domain.Proposals.Events;
using Xunit;

namespace Sangam.Matrimony.UnitTests;

public sealed class ProposalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Sender = Guid.NewGuid();
    private static readonly Guid Recipient = Guid.NewGuid();

    [Fact]
    public void Sending_raises_an_event_and_starts_pending()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, "Would love to connect.", Now);

        proposal.Status.Should().Be(ProposalStatus.Pending);
        proposal.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProposalSentDomainEvent>();
    }

    [Fact]
    public void A_member_cannot_propose_to_themselves()
    {
        var act = () => Proposal.Send(TenantId, Sender, Sender, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_recipient_can_accept()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);
        proposal.ClearDomainEvents();

        proposal.Respond(accept: true, Recipient, Now.AddHours(1));

        proposal.Status.Should().Be(ProposalStatus.Accepted);
        proposal.RespondedAt.Should().Be(Now.AddHours(1));
        proposal.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProposalRespondedDomainEvent>();
    }

    [Fact]
    public void The_recipient_can_decline()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);

        proposal.Respond(accept: false, Recipient, Now.AddHours(1));

        proposal.Status.Should().Be(ProposalStatus.Declined);
    }

    [Fact]
    public void Only_the_recipient_may_respond()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);

        var act = () => proposal.Respond(true, Sender, Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_decided_proposal_cannot_be_decided_again()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);
        proposal.Respond(true, Recipient, Now.AddHours(1));

        var act = () => proposal.Respond(false, Recipient, Now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Only_the_sender_may_withdraw()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);

        var act = () => proposal.Withdraw(Recipient, Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_sender_can_withdraw_while_pending()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);
        proposal.ClearDomainEvents();

        proposal.Withdraw(Sender, Now.AddHours(1));

        proposal.Status.Should().Be(ProposalStatus.Withdrawn);
        proposal.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProposalWithdrawnDomainEvent>();
    }

    [Fact]
    public void An_accepted_proposal_cannot_be_withdrawn()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, null, Now);
        proposal.Respond(true, Recipient, Now.AddHours(1));

        var act = () => proposal.Withdraw(Sender, Now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DeIdentifying_a_pending_proposal_withdraws_it_and_strips_the_message()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, "A private note.", Now);

        proposal.DeIdentify(Now.AddDays(1));

        proposal.Status.Should().Be(ProposalStatus.Withdrawn);
        proposal.Message.Should().BeNull();
    }

    [Fact]
    public void DeIdentifying_a_decided_proposal_keeps_its_outcome()
    {
        var proposal = Proposal.Send(TenantId, Sender, Recipient, "A private note.", Now);
        proposal.Respond(true, Recipient, Now.AddHours(1));

        proposal.DeIdentify(Now.AddDays(1));

        // The outcome (Accepted) is what the other party's own history relies
        // on - de-identifying strips the message, not the fact of acceptance.
        proposal.Status.Should().Be(ProposalStatus.Accepted);
        proposal.Message.Should().BeNull();
    }
}
