using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Commands.SetProductAvailability;

/// <summary>Takes a product off sale, or puts it back - never deletes, so a past order still names a real product.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record SetProductAvailabilityCommand(
    Guid BusinessId, Guid ProductId, bool Available) : ICommand<BusinessResponse>;

public sealed class SetProductAvailabilityCommandHandler(
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<SetProductAvailabilityCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(
        SetProductAvailabilityCommand command, CancellationToken cancellationToken)
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

        var now = clock.UtcNow;

        if (!business.SetProductAvailability(command.ProductId, command.Available, now))
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Product.NotFound", "No such product on this business."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}
