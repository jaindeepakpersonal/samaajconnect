using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Groups;
using Sangam.VolunteerGroups.Application.Elections;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.OpenElection;

/// <summary>
/// Sets an election up for one position in one group. It stays a Draft until
/// the president opens nominations.
/// </summary>
/// <remarks>
/// VolunteerGroups.Lead is the outer gate, held by every member; being this
/// group's president is the inner one, checked against the data here - the
/// same shape every other president-only command in this service already
/// uses.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record OpenElectionCommand(
    Guid GroupId,
    string PositionTitle,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt) : ICommand<ElectionResponse>;

public sealed class OpenElectionCommandValidator : AbstractValidator<OpenElectionCommand>
{
    public OpenElectionCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.PositionTitle).NotEmpty().MaximumLength(100);

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        // Voting on a ballot still being nominated to means early and late
        // voters see different ballots - the same rule celebrity-voting-
        // service's own CreateCampaignCommandValidator already enforces.
        RuleFor(x => x.VotingStartAt)
            .GreaterThanOrEqualTo(x => x.NominationEndAt)
            .WithMessage("Voting must start after nominations close.");
    }
}

public sealed class OpenElectionCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<OpenElectionCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        OpenElectionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ElectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        // "Not found", the same answer every other president-only command in
        // this service gives a non-president - it does not confirm the group
        // exists to somebody who may not run it.
        if (!group.CanRunElection(actorId, currentUser.IsGroupAdmin(), command.PositionTitle))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var now = clock.UtcNow;

        var election = GroupElection.Open(
            group.TenantId,
            group.Id,
            command.PositionTitle,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            now);

        elections.Add(election);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
