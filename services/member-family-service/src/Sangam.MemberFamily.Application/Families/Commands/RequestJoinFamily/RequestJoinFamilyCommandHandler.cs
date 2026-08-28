using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Application.Families.Commands.RequestJoinFamily;

public sealed class RequestJoinFamilyCommandHandler(
    IFamilyRepository families,
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RequestJoinFamilyCommand, Result<FamilyResponse>>
{
    /// <summary>
    /// One message for a wrong code and for a code belonging to another Samaaj.
    /// Telling them apart would let someone confirm a family code exists
    /// somewhere on the platform by trying it here.
    /// </summary>
    private static Result<FamilyResponse> NoSuchFamily() =>
        Result.Failure<FamilyResponse>(
            Error.NotFound("Family.NotFound", "No family matches that code."));

    public async Task<Result<FamilyResponse>> Handle(
        RequestJoinFamilyCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<FamilyResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await profiles.GetByIdAsync(memberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<FamilyResponse>(Error.NotFound(
                "Member.ProfileNotReady", "Your profile is still being set up."));
        }

        if (await families.GetForMemberAsync(memberId, cancellationToken) is not null)
        {
            return Result.Failure<FamilyResponse>(Error.Conflict(
                "Family.AlreadyBelongs", "You already belong to a family, or have asked to join one."));
        }

        var family = await families.GetByCodeAsync(command.FamilyCode.Trim().ToUpperInvariant(), cancellationToken);

        if (family is null)
        {
            return NoSuchFamily();
        }

        // The IDOR guard on the write path: a code is unique per Samaaj, so a
        // code from another Samaaj must not admit anyone here.
        if (tenantContext.TenantId is { } tenantId && family.TenantId != tenantId)
        {
            return NoSuchFamily();
        }

        var request = family.RequestJoin(
            memberId, Enum.Parse<Relationship>(command.Relationship, ignoreCase: true), clock.UtcNow);

        if (request is null)
        {
            return Result.Failure<FamilyResponse>(Error.Conflict(
                "Family.AlreadyRequested", "You have already asked to join this family."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(family.ToResponse(memberId, [profile]));
    }
}
