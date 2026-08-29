using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;
using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.CastVote;

/// <summary>
/// Casts one member's vote.
/// </summary>
/// <remarks>
/// The most contended write path on the platform, and the only one where
/// SERVICES.md calls correctness under concurrency a requirement rather than a
/// nice-to-have. Three things follow from that, and none of them is optional.
///
/// <b>The unique index is the guarantee.</b> Not the check below, and not a
/// distributed lock. Two requests from the same member arriving in the same
/// millisecond at the close of voting both pass any check-then-insert; only the
/// database can refuse the second, and it does, because
/// <c>(CampaignId, VoterMemberId)</c> is unique. SERVICES.md offers "a Redis
/// atomic lock <i>or</i> a unique DB constraint"; the constraint is strictly
/// stronger, since a lock has to decide what to do when Redis is unreachable
/// and every answer to that is worse than not needing one.
///
/// <b>The check above it is a courtesy.</b> It exists so the ordinary case —
/// somebody pressing the button twice — gets "you have already voted" rather
/// than a database error surfacing as a 500. It is not load-bearing and must
/// not be mistaken for the guarantee.
///
/// <b>The insert is its own transaction.</b> `TryCastAsync` writes on its own
/// connection rather than the request's, so voters are not serialised against
/// one another by a transaction held open around a campaign read.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record CastVoteCommand(Guid CampaignId, Guid CandidateId)
    : ICommand<CastVoteResponse>;

/// <summary>
/// <paramref name="Accepted"/> is false when this member had already voted.
/// Reported as success: pressing the button twice should not look like a
/// failure, and the response says what they hold either way.
/// </summary>
public sealed record CastVoteResponse(Guid CampaignId, Guid CandidateId, bool Accepted);

public sealed class CastVoteCommandValidator : AbstractValidator<CastVoteCommand>
{
    public CastVoteCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class CastVoteCommandHandler(
    ICampaignRepository campaigns,
    IVoteRepository votes,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<CastVoteCommandHandler> logger)
    : IRequestHandler<CastVoteCommand, Result<CastVoteResponse>>
{
    public async Task<Result<CastVoteResponse>> Handle(
        CastVoteCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } voterId)
        {
            return Result.Failure<CastVoteResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);

        if (campaign is null
            || (tenantContext.TenantId is { } tenantId && campaign.TenantId != tenantId))
        {
            return Result.Failure<CastVoteResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!campaign.AcceptsVotes(now))
        {
            return Result.Failure<CastVoteResponse>(Error.Conflict(
                "Campaign.VotingClosed", "Voting in this campaign is not open."));
        }

        var candidate = campaign.FindCandidate(command.CandidateId);

        if (candidate is null || candidate.Status != CandidateStatus.Approved)
        {
            // Not on the ballot. A nomination nobody approved is not something
            // anyone can vote for.
            return Result.Failure<CastVoteResponse>(
                Error.NotFound("Candidate.NotFound", "No such candidate on this ballot."));
        }

        if (candidate.MemberId == voterId)
        {
            return Result.Failure<CastVoteResponse>(Error.Conflict(
                "Vote.Self", "You cannot vote for yourself."));
        }

        // The courtesy check. See the remarks above: this is for the error
        // message, not for correctness.
        var existing = await votes.FindForVoterAsync(campaign.Id, voterId, cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new CastVoteResponse(campaign.Id, existing.CandidateId, false));
        }

        var accepted = await votes.TryCastAsync(
            new Vote(campaign.Id, candidate.Id, voterId, now), cancellationToken);

        if (!accepted)
        {
            // The index refused it: another request from this member won the
            // race. That is the design working, not an error.
            logger.LogInformation(
                "Concurrent vote refused by the unique index for campaign {CampaignId}",
                campaign.Id);

            var theirs = await votes.FindForVoterAsync(campaign.Id, voterId, cancellationToken);

            return Result.Success(new CastVoteResponse(
                campaign.Id, theirs?.CandidateId ?? candidate.Id, false));
        }

        return Result.Success(new CastVoteResponse(campaign.Id, candidate.Id, true));
    }
}
