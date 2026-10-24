using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Media;
using DomainCompetitionPhoto = Sangam.Competition.Domain.Media.CompetitionPhoto;

namespace Sangam.Competition.Application.Competitions.Commands.CompetitionPhoto;

// A competition's photo (2026-10-24). Setting and removing it is the organiser's,
// gated like UpdateCompetitionCommand on Competition.Manage - but at any stage,
// because a picture is not one of the details members registered against.
// Reading it is GetCompetitionQuery's rule: any signed-in member of the Samaaj.

// ---- Upload ------------------------------------------------------------------

/// <summary>Stores a competition's photo - its page cover - replacing whatever was there.</summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record UploadCompetitionPhotoCommand(Guid CompetitionId, byte[] Bytes) : ICommand<CompetitionResponse>;

public sealed class UploadCompetitionPhotoCommandValidator : AbstractValidator<UploadCompetitionPhotoCommand>
{
    public UploadCompetitionPhotoCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();

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

public sealed class UploadCompetitionPhotoCommandHandler(
    ICompetitionRepository competitions,
    ICompetitionPhotoStore photos,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadCompetitionPhotoCommand, Result<CompetitionResponse>>
{
    public async Task<Result<CompetitionResponse>> Handle(
        UploadCompetitionPhotoCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CompetitionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        // The IDOR guard: the write re-checks the tenant, never trusting the
        // query filter alone (root CLAUDE.md section 6).
        if (competition is null || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<CompetitionResponse>(CompetitionPhotoRules.NoSuchCompetition);
        }

        var photo = DomainCompetitionPhoto.Capture(competition.TenantId, competition.Id, command.Bytes, actorId, clock.UtcNow);

        photos.Add(photo);

        // The previous photo goes in the same transaction, so replacing always
        // leaves exactly one.
        if (competition.SetPhoto(photo.Id) is { } previousId)
        {
            await photos.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(competition.ToResponse(actorId));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>Takes a competition's photo down. Doing it twice is success.</summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record RemoveCompetitionPhotoCommand(Guid CompetitionId) : ICommand<CompetitionResponse>;

public sealed class RemoveCompetitionPhotoCommandValidator : AbstractValidator<RemoveCompetitionPhotoCommand>
{
    public RemoveCompetitionPhotoCommandValidator() => RuleFor(x => x.CompetitionId).NotEmpty();
}

public sealed class RemoveCompetitionPhotoCommandHandler(
    ICompetitionRepository competitions,
    ICompetitionPhotoStore photos,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<RemoveCompetitionPhotoCommand, Result<CompetitionResponse>>
{
    public async Task<Result<CompetitionResponse>> Handle(
        RemoveCompetitionPhotoCommand command, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<CompetitionResponse>(CompetitionPhotoRules.NoSuchCompetition);
        }

        if (competition.RemovePhoto() is { } photoId)
        {
            await photos.RemoveAsync(photoId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(competition.ToResponse(currentUser.UserId));
    }
}

// ---- Read --------------------------------------------------------------------

/// <summary>A competition's photo bytes - for whoever may open the competition (<c>GetCompetitionQuery</c>).</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetCompetitionPhotoQuery(Guid CompetitionId) : IQuery<CompetitionPhotoContent>;

public sealed class GetCompetitionPhotoQueryValidator : AbstractValidator<GetCompetitionPhotoQuery>
{
    public GetCompetitionPhotoQueryValidator() => RuleFor(x => x.CompetitionId).NotEmpty();
}

public sealed class GetCompetitionPhotoQueryHandler(ICompetitionRepository competitions, ICompetitionPhotoStore photos)
    : IRequestHandler<GetCompetitionPhotoQuery, Result<CompetitionPhotoContent>>
{
    public async Task<Result<CompetitionPhotoContent>> Handle(
        GetCompetitionPhotoQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken);

        var photo = competition?.PhotoId is { } photoId
            ? await photos.GetAsync(photoId, cancellationToken)
            : null;

        return photo is null
            ? Result.Failure<CompetitionPhotoContent>(
                Error.NotFound("Competition.NoPhoto", "This competition has no photo."))
            : Result.Success(new CompetitionPhotoContent(photo.Bytes, photo.ContentType, photo.ContentHash, photo.UploadedAt));
    }
}

/// <summary>The bytes to serve, with what a browser needs to cache them.</summary>
public sealed record CompetitionPhotoContent(byte[] Bytes, string ContentType, string ETag, DateTimeOffset UploadedAt);

internal static class CompetitionPhotoRules
{
    public static readonly Error NoSuchCompetition =
        Error.NotFound("Competition.NotFound", "No such competition in this Samaaj.");
}
