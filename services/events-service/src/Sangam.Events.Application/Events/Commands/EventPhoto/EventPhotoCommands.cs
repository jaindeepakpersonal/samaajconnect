using FluentValidation;
using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Events.Commands.EventSchedule;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Media;
using DomainEventPhoto = Sangam.Events.Domain.Media.EventPhoto;

namespace Sangam.Events.Application.Events.Commands.EventPhoto;

// Setting and removing the photo is editing the event, so it is gated exactly
// like UpdateEventCommand and the schedule: Events.Publish, an Area Admin only
// for an event in their own locality, anyone else told there is no such event,
// and a cancelled event refused (EventForEditing).

// ---- Upload ------------------------------------------------------------------

/// <summary>Stores an event's photo - its page cover - replacing whatever was there.</summary>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record UploadEventPhotoCommand(Guid EventId, byte[] Bytes) : ICommand<EventResponse>;

public sealed class UploadEventPhotoCommandValidator : AbstractValidator<UploadEventPhotoCommand>
{
    public UploadEventPhotoCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();

        // Three rules rather than one: "nothing arrived", "too big" and "not a
        // picture" are different problems, and an organiser can only act on the
        // one they are told about.
        RuleFor(x => x.Bytes)
            .NotEmpty()
            .WithMessage("No photo was uploaded.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage($"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        // The declared content type is never consulted - it is a string the
        // uploader chose. The format is read from the bytes.
        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A photo has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadEventPhotoCommandHandler(
    IEventRepository events,
    IEventPhotoStore photos,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadEventPhotoCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        UploadEventPhotoCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var (samaajEvent, refusal) = await EventForEditing.FindAsync(
            events, command.EventId, areaScope, tenantContext, cancellationToken);

        if (samaajEvent is null)
        {
            return Result.Failure<EventResponse>(refusal!);
        }

        var now = clock.UtcNow;
        var photo = DomainEventPhoto.Capture(samaajEvent.TenantId, samaajEvent.Id, command.Bytes, memberId, now);

        photos.Add(photo);

        // The previous photo goes in the same transaction, so replacing always
        // leaves exactly one.
        if (samaajEvent.SetPhoto(photo.Id, now) is { } previousId)
        {
            await photos.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(samaajEvent.ToResponse(memberId, now));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>Takes an event's photo down. Doing it twice is success.</summary>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record RemoveEventPhotoCommand(Guid EventId) : ICommand<EventResponse>;

public sealed class RemoveEventPhotoCommandValidator : AbstractValidator<RemoveEventPhotoCommand>
{
    public RemoveEventPhotoCommandValidator() => RuleFor(x => x.EventId).NotEmpty();
}

public sealed class RemoveEventPhotoCommandHandler(
    IEventRepository events,
    IEventPhotoStore photos,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveEventPhotoCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        RemoveEventPhotoCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var (samaajEvent, refusal) = await EventForEditing.FindAsync(
            events, command.EventId, areaScope, tenantContext, cancellationToken);

        if (samaajEvent is null)
        {
            return Result.Failure<EventResponse>(refusal!);
        }

        var now = clock.UtcNow;

        if (samaajEvent.RemovePhoto(now) is { } photoId)
        {
            await photos.RemoveAsync(photoId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(samaajEvent.ToResponse(memberId, now));
    }
}

// ---- Read --------------------------------------------------------------------

/// <summary>
/// An event's photo bytes - for whoever may open the event (<c>GetEventQuery</c>):
/// any signed-in member for a published one, an organiser for a draft.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetEventPhotoQuery(Guid EventId) : IQuery<EventPhotoContent>;

public sealed class GetEventPhotoQueryValidator : AbstractValidator<GetEventPhotoQuery>
{
    public GetEventPhotoQueryValidator() => RuleFor(x => x.EventId).NotEmpty();
}

public sealed class GetEventPhotoQueryHandler(
    IEventRepository events, IEventPhotoStore photos, ICurrentUser currentUser, IAreaScope areaScope)
    : IRequestHandler<GetEventPhotoQuery, Result<EventPhotoContent>>
{
    public async Task<Result<EventPhotoContent>> Handle(
        GetEventPhotoQuery query,
        CancellationToken cancellationToken)
    {
        var samaajEvent = await events.GetByIdAsync(query.EventId, cancellationToken);

        // A draft's photo is as private as the draft: the same rule GetEventQuery
        // applies, so a guessed id learns nothing from its picture either.
        var maySee = samaajEvent is not null
            && (samaajEvent.Status != Domain.Events.EventStatus.Draft
                || (currentUser.HasPermission(PermissionKeys.EventsPublish)
                    && areaScope.MayAdminister(samaajEvent.LocalityId)));

        var photo = maySee && samaajEvent!.PhotoId is { } photoId
            ? await photos.GetAsync(photoId, cancellationToken)
            : null;

        return photo is null
            ? Result.Failure<EventPhotoContent>(Error.NotFound("Event.NoPhoto", "This event has no photo."))
            : Result.Success(new EventPhotoContent(photo.Bytes, photo.ContentType, photo.ContentHash, photo.UploadedAt));
    }
}

/// <summary>The bytes to serve, with what a browser needs to cache them.</summary>
public sealed record EventPhotoContent(byte[] Bytes, string ContentType, string ETag, DateTimeOffset UploadedAt);
