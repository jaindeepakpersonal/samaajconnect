using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;
using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Competitions.Commands.CreateEntry;

/// <summary>
/// Puts one registered participant into one round. Created by the organiser
/// one at a time - see `Round`'s own remarks for why there is no bulk
/// "advance the top N" operation.
/// </summary>
/// <remarks>
/// <paramref name="ParticipantMemberId"/> is a wire `string`, parsed only in
/// the handler - the same defense-in-depth every other body-bound id on this
/// platform already uses.
/// </remarks>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record CreateEntryCommand(Guid CompetitionId, Guid RoundId, string ParticipantMemberId)
    : ICommand<EntryResponse>;

public sealed class CreateEntryCommandValidator : AbstractValidator<CreateEntryCommand>
{
    public CreateEntryCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.RoundId).NotEmpty();
        RuleFor(x => x.ParticipantMemberId).Must(BeAGuid).WithMessage("Choose a participant.");
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class CreateEntryCommandHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateEntryCommand, Result<EntryResponse>>
{
    public async Task<Result<EntryResponse>> Handle(
        CreateEntryCommand command, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<EntryResponse>(Error.Forbidden(
                "Competition.NoSamaaj", "Select a Samaaj before creating an entry in it."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null || competition.TenantId != tenantId)
        {
            return Result.Failure<EntryResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var round = competition.FindRound(command.RoundId);

        if (round is null)
        {
            return Result.Failure<EntryResponse>(
                Error.NotFound("Round.NotFound", "No such round in this competition."));
        }

        if (round.Status != RoundStatus.Open)
        {
            return Result.Failure<EntryResponse>(Error.Conflict(
                "Round.Closed", "This round is closed; no new entries can be added."));
        }

        // Guaranteed parseable by CreateEntryCommandValidator.BeAGuid.
        var participantId = Guid.Parse(command.ParticipantMemberId);

        if (!competition.IsRegistered(participantId))
        {
            return Result.Failure<EntryResponse>(Error.Conflict(
                "Entry.NotRegistered", "This member is not registered for this competition."));
        }

        var existing = await entries.FindForParticipantInRoundAsync(
            round.Id, participantId, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<EntryResponse>(Error.Conflict(
                "Entry.AlreadyExists", "This member already has an entry in this round."));
        }

        var now = clock.UtcNow;
        var entry = new Entry(tenantId, round.Id, participantId, now);

        entries.Add(entry);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new EntryResponse(entry.Id, entry.ParticipantMemberId, entry.CreatedAt, null, null));
    }
}
