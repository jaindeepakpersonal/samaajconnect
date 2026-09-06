using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Gautras;

namespace Sangam.MasterData.Application.Gautras.Commands.CreateGautra;

/// <summary>Adds a Gautra to the calling Samaaj's own list.</summary>
[RequiresPermission(PermissionKeys.GautraManage)]
public sealed record CreateGautraCommand(string Name) : ICommand<GautraResponse>;

public sealed class CreateGautraCommandValidator : AbstractValidator<CreateGautraCommand>
{
    public CreateGautraCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class CreateGautraCommandHandler(
    IGautraRepository gautras,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateGautraCommand, Result<GautraResponse>>
{
    public async Task<Result<GautraResponse>> Handle(
        CreateGautraCommand command, CancellationToken cancellationToken)
    {
        // A clean failure rather than RequireTenantId()'s exception: a Super
        // Admin holding no tenant override reaches here (GautraManage is also
        // granted to SuperAdmin, via the platform-wide grant every permission
        // gets), and "select a Samaaj first" is a business outcome, not a bug -
        // the same shape CreateCampaignCommandHandler uses for the same reason.
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<GautraResponse>(Error.Forbidden(
                "Gautra.NoSamaaj", "Select a Samaaj before adding a Gautra to it."));
        }

        var gautra = Gautra.Create(tenantId, command.Name, clock.UtcNow);

        gautras.Add(gautra);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(gautra.ToResponse());
    }
}
