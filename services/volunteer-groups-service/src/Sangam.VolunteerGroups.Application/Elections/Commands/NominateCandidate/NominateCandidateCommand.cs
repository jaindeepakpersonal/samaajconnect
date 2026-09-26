using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.NominateCandidate;

/// <summary>
/// A group member puts another group member forward for the position this
/// election is running.
/// </summary>
/// <remarks>
/// <paramref name="MemberId"/> is a string, not a <see cref="Guid"/> - a
/// Guid-typed wire field crashes ASP.NET Core's own JSON model binding with
/// an unhandled 400 the moment it cannot be parsed, before FluentValidation
/// ever runs. Validated by <see cref="NominateCandidateCommandValidator"/>
/// and parsed only in the handler - the same defense-in-depth this session
/// already added to celebrity-voting-service's own
/// <c>NominateCandidateCommand</c> and to events-service's
/// <c>CreateEventCommand</c>.
///
/// The one real difference from celebrity-voting-service's own nomination:
/// the nominee must already be a member of <i>this group</i>, checked
/// against the loaded <c>VolunteerGroup</c> in the handler - celebrity-voting
/// accepts any Samaaj member, because its ballot is Samaaj-wide.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record NominateCandidateCommand(Guid ElectionId, string MemberId)
    : ICommand<NominateResponse>;

/// <summary>
/// <paramref name="Nominated"/> is false when this member was already put
/// forward - reported as success, since the second nominator has done
/// nothing wrong and one candidacy per member is what keeps a vote from
/// splitting.
/// </summary>
public sealed record NominateResponse(
    Guid ElectionId, Guid CandidateId, Guid MemberId, bool Nominated);

public sealed class NominateCandidateCommandValidator : AbstractValidator<NominateCandidateCommand>
{
    public NominateCandidateCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.MemberId).Must(BeAGuid).WithMessage("Choose a member to nominate.");
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class NominateCandidateCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<NominateCandidateCommand, Result<NominateResponse>>
{
    public async Task<Result<NominateResponse>> Handle(
        NominateCandidateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } nominatorId)
        {
            return Result.Failure<NominateResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<NominateResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var group = await groups.GetByIdAsync(election.GroupId, cancellationToken);

        // The nominator must belong to the group this election is for -
        // "all members who are allowed to be part of the group" is the
        // electorate, not every Samaaj member.
        if (group is null || !group.HasMember(nominatorId))
        {
            return Result.Failure<NominateResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        // Guaranteed parseable by NominateCandidateCommandValidator.BeAGuid.
        var memberId = Guid.Parse(command.MemberId);

        if (!group.HasMember(memberId))
        {
            return Result.Failure<NominateResponse>(Error.Conflict(
                "Election.NotAMember", "Only a member of this group can be nominated."));
        }

        // A member holds one role, and the President role moves only with the
        // presidency - so the president winning, say, Secretary would either
        // leave the group headless or leave the seat unfilled. Refused here,
        // before anybody votes for them.
        if (group.IsPresident(memberId) && election.RoleId != group.PresidentRole.Id)
        {
            return Result.Failure<NominateResponse>(Error.Conflict(
                "Election.IsPresident",
                "The president holds the President role, so cannot stand for another one."));
        }

        var now = clock.UtcNow;

        if (!election.AcceptsNominations(now))
        {
            return Result.Failure<NominateResponse>(Error.Conflict(
                "Election.NominationsClosed", "Nominations for this election are not open."));
        }

        var candidate = election.Nominate(memberId, nominatorId, now);

        if (candidate is null)
        {
            // Already put forward. The candidacy that stands is the one to
            // report back, not an empty id.
            var existing = election.FindCandidateForMember(memberId);

            return Result.Success(new NominateResponse(
                election.Id, existing?.Id ?? Guid.Empty, memberId, false));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new NominateResponse(election.Id, candidate.Id, memberId, true));
    }
}
