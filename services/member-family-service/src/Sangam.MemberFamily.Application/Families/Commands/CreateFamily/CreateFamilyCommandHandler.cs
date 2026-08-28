using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Application.Families.Commands.CreateFamily;

public sealed class CreateFamilyCommandHandler(
    IFamilyRepository families,
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CreateFamilyCommand, Result<FamilyResponse>>
{
    /// <summary>
    /// Attempts before giving up on finding an unused code. With a 31-character
    /// alphabet over 8 places, a collision needs an implausible number of
    /// families; the cap exists so a bug cannot turn into an infinite loop.
    /// </summary>
    private const int CodeAttempts = 5;

    public async Task<Result<FamilyResponse>> Handle(
        CreateFamilyCommand command,
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

        // One household per member: belonging to two would make "your family"
        // ambiguous everywhere it is used.
        if (await families.GetForMemberAsync(memberId, cancellationToken) is not null)
        {
            return Result.Failure<FamilyResponse>(Error.Conflict(
                "Family.AlreadyBelongs", "You already belong to a family, or have asked to join one."));
        }

        var code = await GenerateUniqueCodeAsync(cancellationToken);

        if (code is null)
        {
            return Result.Failure<FamilyResponse>(Error.Failure(
                "Family.CodeUnavailable", "Could not allocate a family code. Please try again."));
        }

        var family = Family.Create(profile.TenantId, memberId, code, clock.UtcNow);

        families.Add(family);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(family.ToResponse(memberId, [profile]));
    }

    private async Task<string?> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var candidate = Family.GenerateCode();

            if (!await families.CodeExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }
}
