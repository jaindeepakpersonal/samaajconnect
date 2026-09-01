using FluentValidation;
using MediatR;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Application.Security;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Auctions.Commands.ManageOccasion;

// ---- Create an occasion ------------------------------------------------------

/// <summary>Announces an occasion. Starts as Upcoming, with no Boli under it.</summary>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record CreateOccasionCommand(
    string Title, string? Description, DateOnly OccasionDate) : ICommand<OccasionResponse>;

public sealed class CreateOccasionCommandValidator : AbstractValidator<CreateOccasionCommand>
{
    public CreateOccasionCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.OccasionDate).NotEqual(default(DateOnly));
    }
}

public sealed class CreateOccasionCommandHandler(
    IOccasionRepository occasions,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateOccasionCommand, Result<OccasionResponse>>
{
    public async Task<Result<OccasionResponse>> Handle(
        CreateOccasionCommand command, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure<OccasionResponse>(
                Error.Unauthorized("Tenant.Missing", "This request names no Samaaj."));
        }

        var occasion = BoliOccasion.Create(
            tenantId, command.Title, command.Description, command.OccasionDate, clock.UtcNow);

        occasions.Add(occasion);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BoliMappings.ToResponse(occasion, boliCount: 0));
    }
}

// ---- Define a type -----------------------------------------------------------

/// <summary>Adds a Boli type to an occasion.</summary>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record DefineBoliTypeCommand(Guid OccasionId, string Name, string? Description)
    : ICommand<BoliTypeResponse>;

public sealed class DefineBoliTypeCommandValidator : AbstractValidator<DefineBoliTypeCommand>
{
    public DefineBoliTypeCommandValidator()
    {
        RuleFor(x => x.OccasionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class DefineBoliTypeCommandHandler(
    IOccasionRepository occasions,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext)
    : IRequestHandler<DefineBoliTypeCommand, Result<BoliTypeResponse>>
{
    public async Task<Result<BoliTypeResponse>> Handle(
        DefineBoliTypeCommand command, CancellationToken cancellationToken)
    {
        var occasion = await occasions.GetByIdAsync(command.OccasionId, cancellationToken);

        if (occasion is null
            || (tenantContext.TenantId is { } tenantId && occasion.TenantId != tenantId))
        {
            return Result.Failure<BoliTypeResponse>(
                Error.NotFound("Occasion.NotFound", "No such occasion in this Samaaj."));
        }

        var type = occasion.DefineType(command.Name, command.Description);

        if (type is null)
        {
            return Result.Failure<BoliTypeResponse>(Error.Conflict(
                "BoliType.Duplicate",
                "This occasion already has a type by that name."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new BoliTypeResponse(type.Id, type.Name, type.Description));
    }
}

// ---- Move an occasion --------------------------------------------------------

/// <summary>Activates or closes an occasion.</summary>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record MoveOccasionCommand(Guid OccasionId, string Status)
    : ICommand<OccasionResponse>;

public sealed class MoveOccasionCommandValidator : AbstractValidator<MoveOccasionCommand>
{
    public MoveOccasionCommandValidator()
    {
        RuleFor(x => x.OccasionId).NotEmpty();

        RuleFor(x => x.Status)
            .Must(status => Enum.TryParse<OccasionStatus>(status, ignoreCase: true, out _))
            .WithMessage("Status must be one of Upcoming, Active or Closed.");
    }
}

public sealed class MoveOccasionCommandHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<MoveOccasionCommand, Result<OccasionResponse>>
{
    public async Task<Result<OccasionResponse>> Handle(
        MoveOccasionCommand command, CancellationToken cancellationToken)
    {
        var occasion = await occasions.GetByIdAsync(command.OccasionId, cancellationToken);

        if (occasion is null
            || (tenantContext.TenantId is { } tenantId && occasion.TenantId != tenantId))
        {
            return Result.Failure<OccasionResponse>(
                Error.NotFound("Occasion.NotFound", "No such occasion in this Samaaj."));
        }

        var status = Enum.Parse<OccasionStatus>(command.Status, ignoreCase: true);

        if (!occasion.MoveTo(status, clock.UtcNow))
        {
            return Result.Failure<OccasionResponse>(Error.Conflict(
                "Occasion.BadTransition",
                $"An occasion cannot move from {occasion.Status} to {status}."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var lots = await boli.ListForOccasionAsync(occasion.Id, cancellationToken);

        return Result.Success(BoliMappings.ToResponse(occasion, lots.Count));
    }
}
