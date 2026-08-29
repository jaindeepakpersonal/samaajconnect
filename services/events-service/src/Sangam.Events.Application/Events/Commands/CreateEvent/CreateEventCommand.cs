using FluentValidation;
using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events.Commands.CreateEvent;

/// <summary>
/// Writes an event down. It is a draft until somebody publishes it.
/// </summary>
/// <remarks>
/// Creating and publishing are separate commands because they are separate
/// decisions: an event exists in someone's head long before the Samaaj should
/// be told about it, and the admin wireframe lists Draft alongside Published
/// for exactly that reason.
/// </remarks>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record CreateEventCommand(
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    string? Venue,
    string OrganizerType,
    Guid? OrganizerId,
    bool RegistrationEnabled,
    int? Capacity) : ICommand<EventResponse>;

public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Venue).MaximumLength(300);

        RuleFor(x => x.OrganizerType)
            .NotEmpty()
            .Must(t => Enum.TryParse<OrganizerType>(t, ignoreCase: true, out _))
            .WithMessage("Organiser must be Samaaj or VolunteerGroup.");

        // A group event with no group named cannot say whose it is, and the
        // member-portal list shows the organiser on every row.
        RuleFor(x => x.OrganizerId)
            .NotEmpty()
            .WithMessage("Name the volunteer group holding this event.")
            .When(x => string.Equals(x.OrganizerType, nameof(Domain.Events.OrganizerType.VolunteerGroup),
                StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.EndAt)
            .GreaterThan(x => x.StartAt)
            .WithMessage("An event cannot end before it starts.")
            .When(x => x.EndAt.HasValue);

        // Zero would be an event nobody can attend, which is a mistake rather
        // than an intention. Leave it null for no limit.
        RuleFor(x => x.Capacity)
            .GreaterThan(0)
            .WithMessage("Capacity must be at least one. Leave it empty for no limit.")
            .When(x => x.Capacity.HasValue);
    }
}

public sealed class CreateEventCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateEventCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        CreateEventCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<EventResponse>(Error.Forbidden(
                "Event.NoSamaaj", "Select a Samaaj before creating an event in it."));
        }

        var created = SamaajEvent.Create(
            tenantId,
            command.Title,
            command.Description,
            command.StartAt,
            command.EndAt,
            command.Venue,
            Enum.Parse<OrganizerType>(command.OrganizerType, ignoreCase: true),
            command.OrganizerId,
            memberId,
            command.RegistrationEnabled,
            command.Capacity,
            clock.UtcNow);

        events.Add(created);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.ToResponse(memberId));
    }
}
