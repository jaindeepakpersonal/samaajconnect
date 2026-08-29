using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;

namespace Sangam.Timeline.Application.Posts.Commands.ReportPost;

/// <summary>
/// Flags a post for the moderators, from the member-portal wireframe's "Report".
/// </summary>
/// <remarks>
/// Reporting removes nothing. One member being able to take a post off a
/// community's timeline would be a heckler's veto, so the report raises a count
/// and puts the post in front of a human; the decision stays with the moderator.
/// </remarks>
[RequiresPermission(PermissionKeys.TimelinePost)]
public sealed record ReportPostCommand(Guid PostId) : ICommand<ReportPostResponse>;

/// <summary>
/// Deliberately does not say whether the report was counted. A member who
/// learns their second report changed nothing has learned that reports are
/// counted per person; one who reports their own post learns it was ignored.
/// Neither is worth telling anyone, and both are things a determined person
/// would use to work out how the queue is fed.
/// </summary>
public sealed record ReportPostResponse(Guid PostId, string Message);

public sealed class ReportPostCommandHandler(
    IPostRepository posts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<ReportPostCommandHandler> logger)
    : IRequestHandler<ReportPostCommand, Result<ReportPostResponse>>
{
    private const string Acknowledgement = "Thank you. The moderators will look at this post.";

    public async Task<Result<ReportPostResponse>> Handle(
        ReportPostCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ReportPostResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var post = await posts.GetByIdAsync(command.PostId, cancellationToken);

        if (post is null
            || (tenantContext.TenantId is { } tenantId && post.TenantId != tenantId))
        {
            return Result.Failure<ReportPostResponse>(
                Error.NotFound("Post.NotFound", "No such post in this Samaaj."));
        }

        if (post.Report(memberId, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Post {PostId} reported; now at {Count} report(s)", post.Id, post.ReportCount);
        }

        return Result.Success(new ReportPostResponse(post.Id, Acknowledgement));
    }
}
