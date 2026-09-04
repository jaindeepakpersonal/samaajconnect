using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Application.Tenants;
using Sangam.IdentityTenant.Domain.Media;

namespace Sangam.IdentityTenant.Application.Media;

// ---- Upload ------------------------------------------------------------------

/// <summary>
/// Stores a Samaaj's logo, replacing whatever was there.
/// </summary>
/// <remarks>
/// A Samaaj Admin may set their own Samaaj's logo, not only a Super Admin — the
/// same reasoning as the grievance contact: it is that community's own mark, and
/// routing every change through the platform operator would make it stale by
/// design. `Tenant.Manage` rather than `AdminUsers.Manage`, because this is a
/// property of the Samaaj rather than of who administers it.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record UploadTenantLogoCommand(Guid TenantId, byte[] Bytes)
    : ICommand<TenantLogoResponse>;

public sealed class UploadTenantLogoCommandValidator : AbstractValidator<UploadTenantLogoCommand>
{
    public UploadTenantLogoCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        // Three rules rather than one, because "nothing arrived", "too big" and
        // "not a picture" are different problems and an administrator can only
        // act on the one they are actually told about.
        RuleFor(x => x.Bytes)
            .NotEmpty()
            .WithMessage("No logo was uploaded.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage(
                $"A logo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        // The declared content type is not consulted anywhere: it is a string
        // the uploader chose. The format is read from the bytes, and that is
        // what is served back.
        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A logo has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadTenantLogoCommandHandler(
    ITenantRepository tenants,
    ILogoStore logos,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UploadTenantLogoCommand, Result<TenantLogoResponse>>
{
    public async Task<Result<TenantLogoResponse>> Handle(
        UploadTenantLogoCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<TenantLogoResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        // A Samaaj Admin may only touch their own Samaaj. A Super Admin belongs
        // to none and may touch any, which is what the tenant override is for.
        // 404 rather than 403, so the answer does not confirm the id is real.
        if (!currentUser.IsInRole(Roles.SuperAdmin) && tenantContext.TenantId != tenant.Id)
        {
            return Result.Failure<TenantLogoResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var logo = TenantLogo.Capture(
            tenant.Id, command.Bytes, currentUser.UserId ?? Guid.Empty, clock.UtcNow);

        logos.Add(logo);

        // The previous logo goes in the same transaction. Replacing has to leave
        // one: a failure between the two writes that kept both would leave bytes
        // nothing refers to, which no later path would clean up.
        var replaced = tenant.SetLogo(logo.Id);

        if (replaced is { } previousId)
        {
            await logos.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantLogoResponse(
            tenant.Id, logo.ContentType, logo.ByteSize, logo.UploadedAt));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>Takes a Samaaj's logo down. Idempotent.</summary>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record RemoveTenantLogoCommand(Guid TenantId) : ICommand<Unit>;

public sealed class RemoveTenantLogoCommandValidator : AbstractValidator<RemoveTenantLogoCommand>
{
    public RemoveTenantLogoCommandValidator() => RuleFor(x => x.TenantId).NotEmpty();
}

public sealed class RemoveTenantLogoCommandHandler(
    ITenantRepository tenants,
    ILogoStore logos,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<RemoveTenantLogoCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveTenantLogoCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<Unit>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        if (!currentUser.IsInRole(Roles.SuperAdmin) && tenantContext.TenantId != tenant.Id)
        {
            return Result.Failure<Unit>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var removed = tenant.RemoveLogo();

        if (removed is { } logoId)
        {
            await logos.RemoveAsync(logoId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}

/// <summary>What an upload answers with. Never the bytes it was just sent.</summary>
public sealed record TenantLogoResponse(
    Guid TenantId,
    string ContentType,
    int ByteSize,
    DateTimeOffset UploadedAt);
