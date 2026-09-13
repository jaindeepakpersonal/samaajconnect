using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;

namespace Sangam.Competition.Application.Competitions.Commands.WithdrawRegistration;

/// <summary>The caller's own withdrawal.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record WithdrawRegistrationCommand(Guid CompetitionId) : ICommand<RegistrationResponse>;

public sealed class WithdrawRegistrationCommandValidator
    : AbstractValidator<WithdrawRegistrationCommand>
{
    public WithdrawRegistrationCommandValidator() => RuleFor(x => x.CompetitionId).NotEmpty();
}

public sealed class WithdrawRegistrationCommandHandler(
    ICompetitionRepository competitions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<WithdrawRegistrationCommand, Result<RegistrationResponse>>
{
    public async Task<Result<RegistrationResponse>> Handle(
        WithdrawRegistrationCommand command, CancellationToken cancellationToken)
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

        if (!competition.Withdraw(memberId, clock.UtcNow))
        {
            return Result.Failure<RegistrationResponse>(Error.Conflict(
                "Registration.NotFound", "You are not registered for this competition."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegistrationResponse(competition.Id, memberId, Registered: false));
    }
}
