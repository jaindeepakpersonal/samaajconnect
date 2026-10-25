using FluentValidation;
using MediatR;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Application.Security;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Auctions.Commands.ManageOccasion;

// ---- Correct an occasion ------------------------------------------------------

/// <summary>
/// Corrects an occasion's title, description, date and locality - the admin
/// panel's edit popup. Refused (409) once the occasion is closed.
/// </summary>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record UpdateOccasionCommand(
    Guid OccasionId, string Title, string? Description, DateOnly OccasionDate, Guid? LocalityId,
    string? Venue = null, Guid? CityId = null)
    : ICommand<OccasionResponse>;

public sealed class UpdateOccasionCommandValidator : AbstractValidator<UpdateOccasionCommand>
{
    public UpdateOccasionCommandValidator()
    {
        RuleFor(x => x.OccasionId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Venue).MaximumLength(300);
        RuleFor(x => x.OccasionDate).NotEqual(default(DateOnly));
    }
}

public sealed class UpdateOccasionCommandHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateOccasionCommand, Result<OccasionResponse>>
{
    public async Task<Result<OccasionResponse>> Handle(
        UpdateOccasionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<OccasionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var occasion = await occasions.GetByIdAsync(command.OccasionId, cancellationToken);

        if (occasion is null)
        {
            return Result.Failure<OccasionResponse>(
                Error.NotFound("Occasion.NotFound", "No such occasion in this Samaaj."));
        }

        if (!occasion.Update(
                command.Title, command.Description, command.OccasionDate, command.LocalityId, actorId, clock.UtcNow,
                command.Venue, command.CityId))
        {
            return Result.Failure<OccasionResponse>(Error.Conflict(
                "Occasion.Closed", "This occasion is closed and its results announced, so it can no longer be edited."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var lots = await boli.ListForOccasionAsync(occasion.Id, cancellationToken);

        return Result.Success(BoliMappings.ToResponse(occasion, boliCount: lots.Count));
    }
}

// ---- Create an occasion ------------------------------------------------------

/// <summary>
/// Announces an occasion. Starts as Upcoming, with no Boli under it.
/// </summary>
/// <remarks>
/// <paramref name="LocalityId"/> is optional - a Samaaj too small to split
/// its Boli by locality leaves it null and runs one Samaaj-wide occasion, as
/// before this field existed. It is a loose reference into
/// master-data-service's <c>Locality</c>, with no referential integrity
/// across the service boundary and no authorization effect in this phase.
/// </remarks>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record CreateOccasionCommand(
    string Title, string? Description, DateOnly OccasionDate, Guid? LocalityId = null,
    string? Venue = null, Guid? CityId = null)
    : ICommand<OccasionResponse>;

public sealed class CreateOccasionCommandValidator : AbstractValidator<CreateOccasionCommand>
{
    public CreateOccasionCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Venue).MaximumLength(300);
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
            tenantId, command.Title, command.Description, command.OccasionDate, clock.UtcNow,
            command.LocalityId, command.Venue, command.CityId);

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
