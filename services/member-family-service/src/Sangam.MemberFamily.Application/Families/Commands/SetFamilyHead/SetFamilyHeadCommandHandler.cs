using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Families.Commands.SetFamilyHead;

public sealed class SetFamilyHeadCommandHandler(
    IFamilyRepository families,
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<SetFamilyHeadCommand, Result<FamilyResponse>>
{
    public async Task<Result<FamilyResponse>> Handle(
        SetFamilyHeadCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<FamilyResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (family is null)
        {
            return Result.Failure<FamilyResponse>(Error.NotFound(
                "Family.None", "You do not belong to a family yet."));
        }

        // IDOR guard on the write path, re-checked rather than left to the
        // query filter (SECURITY-CHECKLIST.md).
        if (tenantContext.TenantId is { } tenantId && family.TenantId != tenantId)
        {
            return Result.Failure<FamilyResponse>(
                Error.NotFound("Family.NotFound", "No such family."));
        }

        // Deliberately not relaxed for Samaaj admins: who a household considers
        // its head is the household's own call.
        if (!family.IsAdmin(memberId))
        {
            return Result.Failure<FamilyResponse>(Error.Forbidden(
                "Family.NotAdmin", "Only the family admin can choose the family head."));
        }

        Guid? head = string.IsNullOrWhiteSpace(command.MemberProfileId)
            ? null
            : Guid.Parse(command.MemberProfileId);

        if (!family.SetHead(head, memberId, clock.UtcNow))
        {
            return Result.Failure<FamilyResponse>(Error.Conflict(
                "Family.HeadNotAMember",
                "The family head has to be an active member of this household."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var memberProfiles = await profiles.ListByIdsAsync(
            [.. family.Members.Select(m => m.MemberProfileId)], cancellationToken);

        return Result.Success(family.ToResponse(memberId, memberProfiles));
    }
}
