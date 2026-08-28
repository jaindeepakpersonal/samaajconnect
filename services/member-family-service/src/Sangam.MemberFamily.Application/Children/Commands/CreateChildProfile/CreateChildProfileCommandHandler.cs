using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Children.Commands.CreateChildProfile;

public sealed class CreateChildProfileCommandHandler(
    IChildRepository children,
    IFamilyRepository families,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CreateChildProfileCommand, Result<ChildResponse>>
{
    public async Task<Result<ChildResponse>> Handle(
        CreateChildProfileCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ChildResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (family is null)
        {
            return Result.Failure<ChildResponse>(Error.NotFound(
                "Family.None", "Create or join a family before adding children to it."));
        }

        // Only the head adds children. Any member of the household being able
        // to would mean a record nobody agreed to, in a family they merely
        // belong to.
        if (!family.IsHead(memberId))
        {
            return Result.Failure<ChildResponse>(Error.Forbidden(
                "Family.NotHead", "Only the head of this family can add children."));
        }

        var child = ChildProfile.Create(
            family.TenantId,
            family.Id,
            command.FullName,
            command.DateOfBirth,
            ParseGender(command.Gender),
            command.PhotoUrl,
            // The head is the person attesting; the validator has already
            // refused the request if they did not.
            memberId,
            clock.UtcNow);

        children.Add(child);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(child.ToResponse(
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), hasPendingConversion: false));
    }

    private static Gender ParseGender(string? value) =>
        Enum.TryParse<Gender>(value, ignoreCase: true, out var parsed) ? parsed : Gender.Unspecified;
}
