using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Businesses;
using Sangam.Businesses.Domain.Media;

namespace Sangam.Businesses.Application.Businesses.Commands.Photos;

// A business's photo and its products' images (2026-10-24). Setting and
// removing one is editing the business, so it is gated exactly like
// UpdateBusinessCommand: the self-scoped role list, the owner only, and anyone
// else told there is no such business. Reading one is GetBusinessQuery's rule.

// ---- Upload ------------------------------------------------------------------

/// <summary>Stores a business's photo, replacing whatever was there.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record UploadBusinessPhotoCommand(Guid BusinessId, byte[] Bytes) : ICommand<BusinessResponse>;

public sealed class UploadBusinessPhotoCommandValidator : AbstractValidator<UploadBusinessPhotoCommand>
{
    public UploadBusinessPhotoCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.Bytes).SetValidator(new ImageBytesValidator());
    }
}

/// <summary>Stores a product's image, replacing whatever was there.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record UploadProductPhotoCommand(Guid BusinessId, Guid ProductId, byte[] Bytes) : ICommand<BusinessResponse>;

public sealed class UploadProductPhotoCommandValidator : AbstractValidator<UploadProductPhotoCommand>
{
    public UploadProductPhotoCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Bytes).SetValidator(new ImageBytesValidator());
    }
}

/// <summary>
/// Three rules rather than one: "nothing arrived", "too big" and "not a
/// picture" are different problems, and an owner can only act on the one they
/// are told about. The declared content type is never consulted.
/// </summary>
internal sealed class ImageBytesValidator : AbstractValidator<byte[]>
{
    public ImageBytesValidator()
    {
        RuleFor(bytes => bytes)
            .NotEmpty()
            .WithMessage("No photo was uploaded.");

        RuleFor(bytes => bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage($"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        RuleFor(bytes => bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A photo has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadBusinessPhotoCommandHandler(
    IBusinessRepository businesses,
    IBusinessImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadBusinessPhotoCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(UploadBusinessPhotoCommand command, CancellationToken cancellationToken)
    {
        var (business, memberId, refusal) = await OwnedBusiness.FindAsync(
            businesses, command.BusinessId, currentUser, tenantContext, cancellationToken);

        if (business is null)
        {
            return Result.Failure<BusinessResponse>(refusal!);
        }

        var now = clock.UtcNow;
        var image = BusinessImage.Capture(business.TenantId, business.Id, command.Bytes, memberId, now);

        images.Add(image);

        // The previous photo goes in the same transaction, so replacing always
        // leaves exactly one.
        if (business.SetPhoto(image.Id, now) is { } previousId)
        {
            await images.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}

public sealed class UploadProductPhotoCommandHandler(
    IBusinessRepository businesses,
    IBusinessImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadProductPhotoCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(UploadProductPhotoCommand command, CancellationToken cancellationToken)
    {
        var (business, memberId, refusal) = await OwnedBusiness.FindAsync(
            businesses, command.BusinessId, currentUser, tenantContext, cancellationToken);

        if (business is null)
        {
            return Result.Failure<BusinessResponse>(refusal!);
        }

        if (business.FindProduct(command.ProductId) is null)
        {
            return Result.Failure<BusinessResponse>(OwnedBusiness.NoSuchProduct);
        }

        var now = clock.UtcNow;
        var image = BusinessImage.Capture(business.TenantId, business.Id, command.Bytes, memberId, now);

        images.Add(image);
        business.SetProductPhoto(command.ProductId, image.Id, now, out var previousId);

        if (previousId is { } replaced)
        {
            await images.RemoveAsync(replaced, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>Takes a business's photo down. Doing it twice is success.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record RemoveBusinessPhotoCommand(Guid BusinessId) : ICommand<BusinessResponse>;

public sealed class RemoveBusinessPhotoCommandValidator : AbstractValidator<RemoveBusinessPhotoCommand>
{
    public RemoveBusinessPhotoCommandValidator() => RuleFor(x => x.BusinessId).NotEmpty();
}

/// <summary>Takes a product's image down. Doing it twice is success.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record RemoveProductPhotoCommand(Guid BusinessId, Guid ProductId) : ICommand<BusinessResponse>;

public sealed class RemoveProductPhotoCommandValidator : AbstractValidator<RemoveProductPhotoCommand>
{
    public RemoveProductPhotoCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
    }
}

public sealed class RemoveBusinessPhotoCommandHandler(
    IBusinessRepository businesses,
    IBusinessImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveBusinessPhotoCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(RemoveBusinessPhotoCommand command, CancellationToken cancellationToken)
    {
        var (business, _, refusal) = await OwnedBusiness.FindAsync(
            businesses, command.BusinessId, currentUser, tenantContext, cancellationToken);

        if (business is null)
        {
            return Result.Failure<BusinessResponse>(refusal!);
        }

        if (business.RemovePhoto(clock.UtcNow) is { } photoId)
        {
            await images.RemoveAsync(photoId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}

public sealed class RemoveProductPhotoCommandHandler(
    IBusinessRepository businesses,
    IBusinessImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveProductPhotoCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(RemoveProductPhotoCommand command, CancellationToken cancellationToken)
    {
        var (business, _, refusal) = await OwnedBusiness.FindAsync(
            businesses, command.BusinessId, currentUser, tenantContext, cancellationToken);

        if (business is null)
        {
            return Result.Failure<BusinessResponse>(refusal!);
        }

        if (!business.RemoveProductPhoto(command.ProductId, clock.UtcNow, out var previousId))
        {
            return Result.Failure<BusinessResponse>(OwnedBusiness.NoSuchProduct);
        }

        if (previousId is { } photoId)
        {
            await images.RemoveAsync(photoId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}

// ---- Read --------------------------------------------------------------------

/// <summary>
/// A business's photo bytes, or one of its products' - for whoever may open the
/// business (<c>GetBusinessQuery</c>): any signed-in member for an active one,
/// its owner otherwise. A product taken off sale shows its image only to the
/// owner, as the product itself is shown only to them.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBusinessImageQuery(Guid BusinessId, Guid? ProductId) : IQuery<BusinessImageContent>;

public sealed class GetBusinessImageQueryValidator : AbstractValidator<GetBusinessImageQuery>
{
    public GetBusinessImageQueryValidator() => RuleFor(x => x.BusinessId).NotEmpty();
}

public sealed class GetBusinessImageQueryHandler(
    IBusinessRepository businesses, IBusinessImageStore images, ICurrentUser currentUser)
    : IRequestHandler<GetBusinessImageQuery, Result<BusinessImageContent>>
{
    private static readonly Error NoImage = Error.NotFound("Business.NoPhoto", "There is no such photo.");

    public async Task<Result<BusinessImageContent>> Handle(GetBusinessImageQuery query, CancellationToken cancellationToken)
    {
        var business = await businesses.GetByIdAsync(query.BusinessId, cancellationToken);
        var isOwner = business is not null && currentUser.UserId == business.OwnerMemberId;

        if (business is null || (!business.IsActive && !isOwner))
        {
            return Result.Failure<BusinessImageContent>(NoImage);
        }

        Guid? imageId;

        if (query.ProductId is { } productId)
        {
            var product = business.FindProduct(productId);
            imageId = product is not null && (product.IsAvailable || isOwner) ? product.PhotoId : null;
        }
        else
        {
            imageId = business.PhotoId;
        }

        var image = imageId is { } id ? await images.GetAsync(id, cancellationToken) : null;

        return image is null
            ? Result.Failure<BusinessImageContent>(NoImage)
            : Result.Success(new BusinessImageContent(image.Bytes, image.ContentType, image.ContentHash, image.UploadedAt));
    }
}

/// <summary>The bytes to serve, with what a browser needs to cache them.</summary>
public sealed record BusinessImageContent(byte[] Bytes, string ContentType, string ETag, DateTimeOffset UploadedAt);

internal static class OwnedBusiness
{
    public static readonly Error NoSuchBusiness = Error.NotFound("Business.NotFound", "No such business.");

    public static readonly Error NoSuchProduct = Error.NotFound("Product.NotFound", "No such product on this business.");

    /// <summary>
    /// The business, when the caller owns it and it is in their Samaaj. Anyone
    /// else's is "not found" - whether a business exists behind that id is the
    /// owner's own business. The IDOR guard is the tenant check as well as the
    /// owner check (root CLAUDE.md section 6).
    /// </summary>
    public static async Task<(Business? Business, Guid MemberId, Error? Refusal)> FindAsync(
        IBusinessRepository businesses,
        Guid businessId,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return (null, Guid.Empty, Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var business = await businesses.GetByIdAsync(businessId, cancellationToken);

        if (business is null
            || business.OwnerMemberId != memberId
            || (tenantContext.TenantId is { } tenantId && business.TenantId != tenantId))
        {
            return (null, memberId, NoSuchBusiness);
        }

        return (business, memberId, null);
    }
}
