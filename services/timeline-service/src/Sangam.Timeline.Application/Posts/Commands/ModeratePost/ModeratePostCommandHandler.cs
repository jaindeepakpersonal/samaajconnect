using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Application.Posts.Commands.ModeratePost;

public sealed class ModeratePostCommandHandler(
    IPostRepository posts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<ModeratePostCommandHandler> logger)
    : IRequestHandler<ModeratePostCommand, Result<PostResponse>>
{
    public async Task<Result<PostResponse>> Handle(
        ModeratePostCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<PostResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var post = await posts.GetByIdAsync(command.PostId, cancellationToken);

        if (post is null)
        {
            return Result.Failure<PostResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        // The IDOR guard CLAUDE.md §6 requires on a write path, re-checked
        // rather than left to the query filter. TenantWriteGuard would catch it
        // at save time; a 404 is a better answer than an exception.
        if (tenantContext.TenantId is { } tenantId && post.TenantId != tenantId)
        {
            return Result.Failure<PostResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        var decision = Enum.Parse<ModerationDecision>(command.Decision, ignoreCase: true);

        if (!post.Moderate(decision, actorId, command.Reason, clock.UtcNow))
        {
            // Already in that state. Reported as success with the post as it
            // stands: two moderators reaching the same conclusion is agreement,
            // not a conflict worth an error.
            return Result.Success(post.ToResponse(actorId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Post {PostId} {Decision} by {ActorId}", post.Id, decision, actorId);

        return Result.Success(post.ToResponse(actorId));
    }
}
