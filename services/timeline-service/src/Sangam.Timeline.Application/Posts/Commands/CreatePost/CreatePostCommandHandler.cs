using MediatR;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;
using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Application.Posts.Commands.CreatePost;

public sealed class CreatePostCommandHandler(
    IPostRepository posts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreatePostCommand, Result<PostResponse>>
{
    public async Task<Result<PostResponse>> Handle(
        CreatePostCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<PostResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            // A platform account posting to "no Samaaj" has nowhere to post to.
            return Result.Failure<PostResponse>(Error.Forbidden(
                "Timeline.NoSamaaj", "Select a Samaaj before posting to its timeline."));
        }

        // Announcements skip the queue, so asking for one is asking to publish
        // without review. Only somebody who could approve their own post anyway
        // may do it.
        if (command.AsAnnouncement && !currentUser.HasPermission(PermissionKeys.TimelineModerate))
        {
            return Result.Failure<PostResponse>(Error.Forbidden(
                "Timeline.NotAModerator",
                "Only a moderator can post a Samaaj announcement. Your post will go to the "
                + "moderators for review instead."));
        }

        var post = TimelinePost.Create(
            tenantId,
            memberId,
            command.AsAnnouncement ? PostType.Announcement : PostType.MemberPost,
            command.Title,
            command.Body,
            clock.UtcNow);

        posts.Add(post);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(post.ToResponse(memberId));
    }
}
