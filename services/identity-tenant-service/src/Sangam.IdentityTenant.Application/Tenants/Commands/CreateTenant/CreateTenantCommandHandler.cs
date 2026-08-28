using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;

public sealed class CreateTenantCommandHandler(
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<CreateTenantCommand, Result<TenantResponse>>
{
    public async Task<Result<TenantResponse>> Handle(
        CreateTenantCommand command,
        CancellationToken cancellationToken)
    {
        var slug = Tenant.NormalizeSlug(command.Slug);

        // Checked here as well as by the unique index: the index is the real
        // guarantee under concurrency, this is the readable error for the
        // overwhelmingly common single-caller case.
        if (await tenants.SlugExistsAsync(slug, cancellationToken))
        {
            return Result.Failure<TenantResponse>(
                Error.Conflict("Tenant.SlugTaken", $"A Samaaj with the slug '{slug}' already exists."));
        }

        if (!string.IsNullOrWhiteSpace(command.Domain))
        {
            var domain = command.Domain.Trim().ToLowerInvariant();

            if (await tenants.DomainExistsAsync(domain, cancellationToken))
            {
                return Result.Failure<TenantResponse>(
                    Error.Conflict("Tenant.DomainTaken", $"The domain '{domain}' is already mapped to a Samaaj."));
            }
        }

        var tenant = Tenant.Create(
            command.Name,
            slug,
            command.Domain,
            command.ContactPerson,
            command.ContactEmail,
            command.EnabledModules,
            clock.UtcNow);

        tenants.Add(tenant);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(tenant.ToResponse());
    }
}
