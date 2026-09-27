using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Trusts.Commands.RenameTrust;

/// <summary>
/// Corrects a Trust's name - the edit the admin panel's Trust popup makes.
/// </summary>
/// <remarks>
/// The locality is not editable. It is the Trust's identity on this platform:
/// one Trust per locality, and a published Boli in that locality opens what its
/// winner owes to this Trust. Moving a Trust would quietly re-point every
/// future collection; a Samaaj that has the wrong locality stands this Trust
/// down and sets up the right one. Renaming to the same name is success and
/// changes nothing.
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RenameTrustCommand(Guid TrustId, string Name) : ICommand<TrustResponse>;

public sealed class RenameTrustCommandValidator : AbstractValidator<RenameTrustCommand>
{
    public RenameTrustCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class RenameTrustCommandHandler(
    ITrustRepository trusts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RenameTrustCommand, Result<TrustResponse>>
{
    public async Task<Result<TrustResponse>> Handle(RenameTrustCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<TrustResponse>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        if (trust.Rename(command.Name, actorId, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(trust.ToResponse());
    }
}
