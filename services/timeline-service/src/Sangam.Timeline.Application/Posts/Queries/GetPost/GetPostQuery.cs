using MediatR;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;

namespace Sangam.Timeline.Application.Posts.Queries.GetPost;

/// <summary>One post with its comments.</summary>
[RequiresPermission(PermissionKeys.TimelinePost)]
public sealed record GetPostQuery(Guid PostId) : IQuery<PostDetailResponse>;

public sealed class GetPostQueryHandler(IPostRepository posts, ICurrentUser currentUser)
    : IRequestHandler<GetPostQuery, Result<PostDetailResponse>>
{
    public async Task<Result<PostDetailResponse>> Handle(
        GetPostQuery query,
        CancellationToken cancellationToken)
    {
        var post = await posts.GetByIdAsync(query.PostId, cancellationToken);

        if (post is null)
        {
            return Result.Failure<PostDetailResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        var viewerId = currentUser.UserId;

        // A post still in the queue is visible to its author and to a
        // moderator, and to nobody else. Anyone else is told it does not
        // exist rather than that they may not see it - the difference
        // confirms that a post with that id is there.
        var maySee = post.IsPubliclyVisible
            || post.AuthorMemberId == viewerId
            || currentUser.HasPermission(PermissionKeys.TimelineModerate);

        return maySee
            ? Result.Success(post.ToDetail(viewerId))
            : Result.Failure<PostDetailResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
    }
}
