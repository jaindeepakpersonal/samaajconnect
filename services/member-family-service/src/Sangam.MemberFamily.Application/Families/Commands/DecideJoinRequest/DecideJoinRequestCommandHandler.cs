using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Families.Commands.DecideJoinRequest;

public sealed class DecideJoinRequestCommandHandler(
    IFamilyRepository families,
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DecideJoinRequestCommand, Result<FamilyResponse>>
{
    public async Task<Result<FamilyResponse>> Handle(
        DecideJoinRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<FamilyResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetByIdAsync(command.FamilyId, cancellationToken);

        if (family is null)
        {
            return Result.Failure<FamilyResponse>(
                Error.NotFound("Family.NotFound", "No such family."));
        }

        // IDOR guard on the write path, re-checked rather than left to the
        // query filter (SECURITY-CHECKLIST.md).
        if (tenantContext.TenantId is { } tenantId && family.TenantId != tenantId)
        {
            return Result.Failure<FamilyResponse>(
                Error.NotFound("Family.NotFound", "No such family."));
        }

        // Deliberately not relaxed for Samaaj admins. Deciding who is in
        // someone's household is the head's call, not an administrative one.
        if (!family.IsHead(memberId))
        {
            return Result.Failure<FamilyResponse>(Error.Forbidden(
                "Family.NotHead", "Only the head of this family can decide join requests."));
        }

        if (!family.DecideJoinRequest(command.RequestId, command.Accept, memberId, clock.UtcNow))
        {
            return Result.Failure<FamilyResponse>(Error.NotFound(
                "Family.RequestNotFound", "That join request is no longer pending."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var memberProfiles = await profiles.SearchAsync(null, null, 200, cancellationToken);

        return Result.Success(family.ToResponse(memberId, memberProfiles));
    }
}
