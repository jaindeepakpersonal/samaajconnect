using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Trusts;

namespace Sangam.Trust.Application.Trusts.Commands.ChangeTrustStatus;

/// <summary>Activates or deactivates a Trust. A repeat call that changes nothing is success.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record ChangeTrustStatusCommand(Guid TrustId, string Status) : ICommand<TrustResponse>;

public sealed class ChangeTrustStatusCommandValidator : AbstractValidator<ChangeTrustStatusCommand>
{
    public ChangeTrustStatusCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<TrustStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Active or Inactive.");
    }
}

public sealed class ChangeTrustStatusCommandHandler(
    ITrustRepository trusts,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<ChangeTrustStatusCommand, Result<TrustResponse>>
{
    public async Task<Result<TrustResponse>> Handle(
        ChangeTrustStatusCommand command,
        CancellationToken cancellationToken)
    {
        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<TrustResponse>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var target = Enum.Parse<TrustStatus>(command.Status, ignoreCase: true);

        if (trust.ChangeStatus(target, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(trust.ToResponse());
    }
}
