using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Media;

namespace Sangam.IdentityTenant.Application.Media;

// ---- Upload ------------------------------------------------------------------

/// <summary>
/// Stores the photo across the top of a Samaaj's members' Home, replacing
/// whatever was there.
/// </summary>
/// <remarks>
/// Who may set it is the logo's rule (<see cref="UploadTenantLogoCommand"/>): a
/// Super Admin, or the Samaaj's own administrator for their own Samaaj. Who may
/// see it is not - see <see cref="GetTenantBannerQuery"/>.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record UploadTenantBannerCommand(Guid TenantId, byte[] Bytes)
    : ICommand<TenantBannerResponse>;

public sealed class UploadTenantBannerCommandValidator : AbstractValidator<UploadTenantBannerCommand>
{
    public UploadTenantBannerCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        // The logo's three rules, for its reason: each is a different problem.
        RuleFor(x => x.Bytes)
            .NotEmpty()
            .WithMessage("No banner was uploaded.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage(
                $"A banner has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A banner has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadTenantBannerCommandHandler(
    ITenantRepository tenants,
    ITenantImageStore images,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UploadTenantBannerCommand, Result<TenantBannerResponse>>
{
    public async Task<Result<TenantBannerResponse>> Handle(
        UploadTenantBannerCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        // Another Samaaj's id is answered as no such Samaaj, as the logo does.
        if (tenant is null
            || (!currentUser.IsInRole(Roles.SuperAdmin) && tenantContext.TenantId != tenant.Id))
        {
            return Result.Failure<TenantBannerResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var banner = TenantImage.Capture(
            tenant.Id, command.Bytes, currentUser.UserId ?? Guid.Empty, clock.UtcNow);

        images.Add(banner);

        // The replaced banner goes in the same transaction, as a replaced logo does.
        if (tenant.SetBanner(banner.Id) is { } previousId)
        {
            await images.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantBannerResponse(
            tenant.Id, banner.ContentType, banner.ByteSize, banner.UploadedAt));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>Takes a Samaaj's banner down. Idempotent.</summary>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record RemoveTenantBannerCommand(Guid TenantId) : ICommand<Unit>;

public sealed class RemoveTenantBannerCommandValidator : AbstractValidator<RemoveTenantBannerCommand>
{
    public RemoveTenantBannerCommandValidator() => RuleFor(x => x.TenantId).NotEmpty();
}

public sealed class RemoveTenantBannerCommandHandler(
    ITenantRepository tenants,
    ITenantImageStore images,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<RemoveTenantBannerCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveTenantBannerCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null
            || (!currentUser.IsInRole(Roles.SuperAdmin) && tenantContext.TenantId != tenant.Id))
        {
            return Result.Failure<Unit>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        if (tenant.RemoveBanner() is { } bannerId)
        {
            await images.RemoveAsync(bannerId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}

/// <summary>What an upload answers with. Never the bytes it was just sent.</summary>
public sealed record TenantBannerResponse(
    Guid TenantId,
    string ContentType,
    int ByteSize,
    DateTimeOffset UploadedAt);
