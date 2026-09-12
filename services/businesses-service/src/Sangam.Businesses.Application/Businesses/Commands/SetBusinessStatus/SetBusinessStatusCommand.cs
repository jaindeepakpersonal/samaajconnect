using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Commands.SetBusinessStatus;

/// <summary>Deactivate or reactivate a business the caller owns. Reversible - deactivating keeps every product and every past order, it simply drops out of the directory.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record SetBusinessStatusCommand(Guid BusinessId, bool Active) : ICommand<BusinessResponse>;

public sealed class SetBusinessStatusCommandHandler(
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<SetBusinessStatusCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(SetBusinessStatusCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<BusinessResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var business = await businesses.GetByIdAsync(command.BusinessId, cancellationToken);

        if (business is null || business.OwnerMemberId != memberId)
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Business.NotFound", "No such business."));
        }

        if (tenantContext.TenantId is { } tenantId && business.TenantId != tenantId)
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Business.NotFound", "No such business."));
        }

        business.SetActive(command.Active, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}
