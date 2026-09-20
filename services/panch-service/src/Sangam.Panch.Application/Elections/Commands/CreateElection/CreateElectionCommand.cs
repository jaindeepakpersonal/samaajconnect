using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Elections.Commands.CreateElection;

/// <summary>
/// Sets an election up. It stays a draft until somebody opens nominations.
/// </summary>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record CreateElectionCommand(
    string Title,
    string? Description,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    string ResultsVisibility) : ICommand<ElectionResponse>;

public sealed class CreateElectionCommandValidator : AbstractValidator<CreateElectionCommand>
{
    public CreateElectionCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        // Voting on a ballot that is still being nominated to means the people
        // who vote early see a different ballot from the people who vote late.
        RuleFor(x => x.VotingStartAt)
            .GreaterThanOrEqualTo(x => x.NominationEndAt)
            .WithMessage("Voting must start after nominations close.");


        RuleFor(x => x.ResultsVisibility)
            .NotEmpty()
            .Must(v => Enum.TryParse<ResultsVisibility>(v, ignoreCase: true, out _))
            .WithMessage("Results visibility must be Live or HiddenUntilClose.");
    }
}

public sealed class CreateElectionCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateElectionCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        CreateElectionCommand command,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<ElectionResponse>(Error.Forbidden(
                "Election.NoSamaaj", "Select a Samaaj before creating an election in it."));
        }

        var election = PanchElection.Create(
            tenantId,
            command.Title,
            command.Description,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            Enum.Parse<ResultsVisibility>(command.ResultsVisibility, ignoreCase: true),
            clock.UtcNow);

        elections.Add(election);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(clock.UtcNow, myChoices: null));
    }
}
