using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Biodata.Commands.WithdrawMatrimonyConsent;

/// <summary>
/// Takes the caller's own biodata out of matrimony search. Deactivates
/// rather than erasing - see `BiodataProfile.WithdrawConsent`'s own remarks.
/// </summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record WithdrawMatrimonyConsentCommand : ICommand<WithdrawMatrimonyConsentResult>;

public sealed record WithdrawMatrimonyConsentResult(bool Withdrawn);

public sealed class WithdrawMatrimonyConsentCommandHandler(
    IBiodataRepository biodata,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<WithdrawMatrimonyConsentCommand, Result<WithdrawMatrimonyConsentResult>>
{
    public async Task<Result<WithdrawMatrimonyConsentResult>> Handle(
        WithdrawMatrimonyConsentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<WithdrawMatrimonyConsentResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await biodata.GetByIdAsync(memberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<WithdrawMatrimonyConsentResult>(
                Error.NotFound("Biodata.NotFound", "You have not opted into matrimony."));
        }

        if (tenantContext.TenantId is { } tenantId && profile.TenantId != tenantId)
        {
            return Result.Failure<WithdrawMatrimonyConsentResult>(
                Error.NotFound("Biodata.NotFound", "You have not opted into matrimony."));
        }

        var wasActive = profile.IsActive;

        profile.WithdrawConsent(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new WithdrawMatrimonyConsentResult(wasActive));
    }
}
