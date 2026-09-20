using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;

namespace Sangam.Panch.Application.Elections.Commands.NominateCandidate;

/// <summary>Puts a member forward. An administrator decides whether they reach the ballot.</summary>
/// <remarks>
/// <paramref name="MemberId"/> is a string, not a <see cref="Guid"/> - a
/// Guid-typed wire field crashes ASP.NET Core's own JSON model binding with an
/// unhandled 400 the moment the value cannot be parsed, before FluentValidation
/// ever runs. Validated by <see cref="NominateCandidateCommandValidator"/> and
/// parsed only in the handler, the platform's usual defense in depth for a
/// body-bound id.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record NominateCandidateCommand(Guid ElectionId, string MemberId)
    : ICommand<NominateResponse>;

/// <summary>
/// <paramref name="Nominated"/> is false when this member was already put
/// forward. Reported as success: the second nominator has done nothing wrong,
/// and one candidacy per member is what keeps a vote from splitting.
/// </summary>
public sealed record NominateResponse(
    Guid ElectionId, Guid CandidateId, Guid MemberId, bool Nominated);

public sealed class NominateCandidateCommandValidator
    : AbstractValidator<NominateCandidateCommand>
{
    public NominateCandidateCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.MemberId).Must(BeAGuid).WithMessage("Choose a member to nominate.");
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class NominateCandidateCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<NominateCandidateCommand, Result<NominateResponse>>
{
    public async Task<Result<NominateResponse>> Handle(
        NominateCandidateCommand command,
        CancellationToken cancellationToken)
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

        var now = clock.UtcNow;

        if (!election.AcceptsNominations(now))
        {
            return Result.Failure<NominateResponse>(Error.Conflict(
                "Election.NominationsClosed", "Nominations for this election are not open."));
        }

        // Guaranteed parseable by NominateCandidateCommandValidator.BeAGuid.
        var memberId = Guid.Parse(command.MemberId);

        var candidate = election.Nominate(memberId, nominatorId, now);

        if (candidate is null)
        {
            // Already put forward. The candidacy that stands is the one to
            // report back, not an empty id.
            var existing = election.Candidates.FirstOrDefault(c => c.MemberId == memberId);

            return Result.Success(new NominateResponse(
                election.Id, existing?.Id ?? Guid.Empty, memberId, false));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new NominateResponse(election.Id, candidate.Id, memberId, true));
    }
}
