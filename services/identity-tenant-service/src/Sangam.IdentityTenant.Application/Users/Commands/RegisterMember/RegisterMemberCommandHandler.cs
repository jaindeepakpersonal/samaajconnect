using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Users.Commands.RegisterMember;

public sealed class RegisterMemberCommandHandler(
    ITenantRepository tenants,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RegisterMemberCommand, Result<RegisterMemberResponse>>
{
    public async Task<Result<RegisterMemberResponse>> Handle(
        RegisterMemberCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetBySlugAsync(
            Tenant.NormalizeSlug(command.TenantSlug), cancellationToken);

        if (tenant is null || tenant.Status == TenantStatus.Archived)
        {
            return Result.Failure<RegisterMemberResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj matches that address."));
        }

        if (tenant.Status != TenantStatus.Active)
        {
            return Result.Failure<RegisterMemberResponse>(
                Error.Conflict("Tenant.NotActive", "This Samaaj is not accepting registrations yet."));
        }

        // If the gateway already resolved a tenant for this request, the form's
        // choice must agree with it. Otherwise a request arriving on one
        // Samaaj's subdomain could quietly create a member in another.
        if (tenantContext.TenantId is { } resolvedTenantId && resolvedTenantId != tenant.Id)
        {
            return Result.Failure<RegisterMemberResponse>(
                Error.Forbidden("Tenant.Mismatch", "That Samaaj does not match the site you are on."));
        }

        var identifier = User.NormalizeIdentifier(command.MobileOrEmail);

        // Checked platform-wide, not per tenant: a member belongs to one Samaaj,
        // and common login could not route deterministically otherwise.
        if (await users.IdentifierExistsAsync(identifier, cancellationToken))
        {
            return Result.Failure<RegisterMemberResponse>(
                Error.Conflict("User.IdentifierTaken", "That mobile number or email is already registered."));
        }

        var user = User.Register(
            tenant.Id,
            identifier,
            command.FullName,
            passwordHasher.Hash(command.Password),
            AuthorizationCatalog.RoleIds.Member,
            clock.UtcNow);

        users.Add(user);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegisterMemberResponse(
            user.Id, tenant.Id, tenant.Slug, user.MobileOrEmail, user.IsContactVerified));
    }
}
