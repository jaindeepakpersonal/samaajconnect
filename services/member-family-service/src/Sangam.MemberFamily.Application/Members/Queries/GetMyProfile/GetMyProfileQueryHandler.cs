using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Members.Queries.GetMyProfile;

public sealed class GetMyProfileQueryHandler(
    IMemberProfileRepository profiles,
    ICurrentUser currentUser)
    : IRequestHandler<GetMyProfileQuery, Result<MyProfileResponse>>
{
    public async Task<Result<MyProfileResponse>> Handle(
        GetMyProfileQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<MyProfileResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await profiles.GetByIdAsync(userId, cancellationToken);

        if (profile is null)
        {
            // The profile is created by a Kafka consumer moments after
            // registration, so a brand-new member can arrive here first. Saying
            // so beats a bare 404 the portal cannot explain.
            return Result.Failure<MyProfileResponse>(Error.NotFound(
                "Member.ProfileNotReady",
                "Your profile is still being set up. Please try again in a moment."));
        }

        return Result.Success(profile.ToOwnerResponse());
    }
}
