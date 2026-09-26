using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Groups;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.UpdateElection;

/// <summary>
/// Corrects an election's position title and windows. Allowed while it is a
/// draft or taking nominations, refused from voting onward - the same rule
/// celebrity-voting-service's own <c>UpdateCampaignCommand</c> uses.
/// </summary>
/// <remarks>
/// This is how nominations that closed too early get reopened: pushing
/// <see cref="NominationEndAt"/> (and, if needed, the voting window after it)
/// out again. There is no separate "reopen" command, because nothing about
/// <c>Status</c> needs to move - an election left at NominationsOpen past its
/// own end date already has <c>AcceptsNominations</c> false with no status to
/// walk back, so extending the date is the whole fix.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record UpdateElectionCommand(
    Guid ElectionId,
    string PositionTitle,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt) : ICommand<ElectionResponse>;

/// <summary>The same rules as opening one, so an edit cannot reach a state opening could not.</summary>
public sealed class UpdateElectionCommandValidator : AbstractValidator<UpdateElectionCommand>
{
    public UpdateElectionCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.PositionTitle).NotEmpty().MaximumLength(100);

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        RuleFor(x => x.VotingStartAt)
            .GreaterThanOrEqualTo(x => x.NominationEndAt)
            .WithMessage("Voting must start after nominations close.");
    }
}

public sealed class UpdateElectionCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateElectionCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        UpdateElectionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ElectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var group = await groups.GetByIdAsync(election.GroupId, cancellationToken);

        // "Not found", the same answer every other president-only command in
        // this service gives a non-president - see OpenElectionCommand.
        if (group is null || !group.CanRunElection(actorId, currentUser.Administers(areaScope, group), election.PositionTitle))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        if (!election.CanBeEdited)
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.NotEditable",
                "Voting has opened, so this election can no longer be changed - group members "
                + "have been told its windows and may already have voted against them."));
        }

        var now = clock.UtcNow;

        election.Update(
            command.PositionTitle,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
