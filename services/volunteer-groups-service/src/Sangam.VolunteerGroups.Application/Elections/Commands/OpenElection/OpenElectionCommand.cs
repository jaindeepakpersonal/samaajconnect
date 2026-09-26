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
/// Sets an election up for one of the group's roles. It stays a Draft until
/// nominations are opened. It fills as many seats as the role has.
/// </summary>
/// <remarks>
/// VolunteerGroups.Lead is the outer gate, held by every member; being this
/// group's president or one of its administrators is the inner one, checked
/// against the data here - see <c>GroupAuthority.CanRunElections</c>.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record OpenElectionCommand(
    Guid GroupId,

    // A string, not a Guid - a Guid-typed wire field crashes model binding
    // with an unhandled 400 before validation runs. Validated, then parsed.
    string RoleId,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt) : ICommand<ElectionResponse>;

public sealed class OpenElectionCommandValidator : AbstractValidator<OpenElectionCommand>
{
    public OpenElectionCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.RoleId)
            .Must(v => Guid.TryParse(v, out var id) && id != Guid.Empty)
            .WithMessage("Choose which role this election is for.");

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
    IAreaScope areaScope,
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
        if (!group.CanRunElections(actorId, currentUser.Administers(areaScope, group)))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var resolved = await ElectionRoles.ResolveAsync(
            group, command.RoleId, elections, exceptElectionId: null, cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<ElectionResponse>(resolved.Error);
        }

        var role = resolved.Value;
        var now = clock.UtcNow;

        var election = GroupElection.Open(
            group.TenantId,
            group.Id,
            role.Id,
            role.Title,
            role.Seats ?? 1,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            now);

        elections.Add(election);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(now, myVoteCandidateIds: []));
    }
}
