using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.SetGrievanceContact;

/// <summary>
/// Names the person a member complains to about how their data is handled
/// (DPDP section 13).
/// </summary>
/// <remarks>
/// A Samaaj Admin can set this for their own Samaaj, not just a Super Admin:
/// the grievance contact is a person in that Samaaj, and routing every change
/// through the platform operator would make it stale by design.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record SetGrievanceContactCommand(
    Guid TenantId,
    string? Name,
    string? Email,
    string? Phone) : ICommand<TenantResponse>;

public sealed class SetGrievanceContactCommandValidator
    : AbstractValidator<SetGrievanceContactCommand>
{
    public SetGrievanceContactCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Phone).MaximumLength(20);

        // Either a way to reach them, or nothing at all. A name with no contact
        // details is not a means of redressal.
        RuleFor(x => x)
            .Must(command =>
                string.IsNullOrWhiteSpace(command.Name)
                || !string.IsNullOrWhiteSpace(command.Email)
                || !string.IsNullOrWhiteSpace(command.Phone))
            .WithMessage("Give an email address or a phone number for the grievance contact.");
    }
}

public sealed class SetGrievanceContactCommandHandler(
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<SetGrievanceContactCommand, Result<TenantResponse>>
{
    public async Task<Result<TenantResponse>> Handle(
        SetGrievanceContactCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<TenantResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        // A Samaaj Admin may only set their own Samaaj's contact. A Super
        // Admin, who belongs to no Samaaj, may set any - which is what the
        // tenant override is for.
        var isSuperAdmin = currentUser.IsInRole(Roles.SuperAdmin);

        if (!isSuperAdmin && tenantContext.TenantId != tenant.Id)
        {
            return Result.Failure<TenantResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        tenant.SetGrievanceContact(command.Name, command.Email, command.Phone);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(tenant.ToResponse());
    }
}
