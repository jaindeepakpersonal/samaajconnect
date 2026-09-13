using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;

namespace Sangam.Competition.Application.Competitions.Commands.RegisterForCompetition;

/// <summary>Registers the caller. Only while registration is open.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RegisterForCompetitionCommand(Guid CompetitionId) : ICommand<RegistrationResponse>;

public sealed class RegisterForCompetitionCommandValidator
    : AbstractValidator<RegisterForCompetitionCommand>
{
    public RegisterForCompetitionCommandValidator() => RuleFor(x => x.CompetitionId).NotEmpty();
}

public sealed class RegisterForCompetitionCommandHandler(
    ICompetitionRepository competitions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RegisterForCompetitionCommand, Result<RegistrationResponse>>
{
    public async Task<Result<RegistrationResponse>> Handle(
        RegisterForCompetitionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<RegistrationResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<RegistrationResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!competition.AcceptsRegistrations(now))
        {
            return Result.Failure<RegistrationResponse>(Error.Conflict(
                "Competition.RegistrationClosed", "Registration for this competition is not open."));
        }

        var wasRegistered = competition.IsRegistered(memberId);

        competition.Register(memberId, now);

        if (!wasRegistered)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new RegistrationResponse(competition.Id, memberId, !wasRegistered));
    }
}
