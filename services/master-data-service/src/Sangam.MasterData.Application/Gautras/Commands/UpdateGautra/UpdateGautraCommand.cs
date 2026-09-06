using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Gautras.Commands.UpdateGautra;

[RequiresPermission(PermissionKeys.GautraManage)]
public sealed record UpdateGautraCommand(Guid Id, string Name) : ICommand<GautraResponse>;

public sealed class UpdateGautraCommandValidator : AbstractValidator<UpdateGautraCommand>
{
    public UpdateGautraCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateGautraCommandHandler(
    IGautraRepository gautras, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    : IRequestHandler<UpdateGautraCommand, Result<GautraResponse>>
{
    public async Task<Result<GautraResponse>> Handle(
        UpdateGautraCommand command, CancellationToken cancellationToken)
    {
        // Read through the repository, which is tenant-filtered by the
        // DbContext's global query filter (root CLAUDE.md §6) - a Samaaj
        // Admin cannot even see another Samaaj's Gautra to rename it.
        var gautra = await gautras.GetByIdAsync(command.Id, cancellationToken);

        if (gautra is null)
        {
            return Result.Failure<GautraResponse>(
                Error.NotFound("Gautra.NotFound", "No such Gautra in this Samaaj."));
        }

        gautra.Update(command.Name, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(gautra.ToResponse());
    }
}
