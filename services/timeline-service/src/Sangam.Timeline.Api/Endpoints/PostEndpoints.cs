using MediatR;
using Sangam.Timeline.Api.Extensions;
using Sangam.Timeline.Application.Posts;
using Sangam.Timeline.Application.Posts.Commands.AddComment;
using Sangam.Timeline.Application.Posts.Commands.CreatePost;
using Sangam.Timeline.Application.Posts.Commands.ModeratePost;
using Sangam.Timeline.Application.Posts.Commands.ReactToPost;
using Sangam.Timeline.Application.Posts.Commands.ReportPost;
using Sangam.Timeline.Application.Posts.Queries.GetFeed;
using Sangam.Timeline.Application.Posts.Queries.GetModerationQueue;
using Sangam.Timeline.Application.Posts.Queries.GetPost;

namespace Sangam.Timeline.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class PostEndpoints
{
    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/timeline").WithTags("Timeline");

        group.MapGet("/posts", async (
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetFeedQuery(limit), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetFeed")
            .WithSummary("The Samaaj's timeline, plus this member's own posts whatever their status.")
            .Produces<IReadOnlyList<PostResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Before "/posts/{id}", or a request for the queue would be read as a
        // post whose id is "moderation-queue".
        group.MapGet("/posts/moderation-queue", async (
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetModerationQueueQuery(limit), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetModerationQueue")
            .WithSummary("Posts awaiting review, and approved posts members have reported.")
            .Produces<IReadOnlyList<ModerationQueueItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/posts/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPostQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetPost")
            .WithSummary("One post with its comments.")
            .Produces<PostDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/posts", async (
                CreatePostRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreatePostCommand(
                    request.Title, request.Body, request.AsAnnouncement);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(post =>
                    Results.Created($"/v1/timeline/posts/{post.Id}", post));
            })
            .RequireAuthorization()
            .WithName("CreatePost")
            .WithSummary("Write a post. A member's goes to the moderators; an announcement does not.")
            .Produces<PostResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/posts/{id:guid}/moderate", async (
                Guid id,
                ModerateRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ModeratePostCommand(id, request.Decision, request.Reason), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ModeratePost")
            .WithSummary("Approve, reject, hide or restore a post.")
            .Produces<PostResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/posts/{id:guid}/comments", async (
                Guid id,
                CommentRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AddCommentCommand(id, request.Body), cancellationToken);

                return result.ToApiResult(comment =>
                    Results.Created($"/v1/timeline/posts/{id}", comment));
            })
            .RequireAuthorization()
            .WithName("AddComment")
            .WithSummary("Comment on an approved post.")
            .Produces<CommentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/posts/{id:guid}/reaction", async (
                Guid id,
                ReactionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ReactToPostCommand(id, request.Reaction), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ReactToPost")
            .WithSummary("Set, change or clear this member's reaction. Sending the same one removes it.")
            .Produces<PostResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/posts/{id:guid}/report", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ReportPostCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ReportPost")
            .WithSummary("Flag a post for the moderators. Removes nothing by itself.")
            .Produces<ReportPostResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// <paramref name="AsAnnouncement"/> asks to publish without review, and is
    /// refused to anyone without Timeline.Moderate.
    /// </summary>
    public sealed record CreatePostRequest(string Title, string Body, bool AsAnnouncement = false);

    /// <summary>
    /// <paramref name="Decision"/> is required and has no default: a moderation
    /// endpoint whose safest value is implicit is one where a mistyped request
    /// quietly publishes something.
    /// </summary>
    public sealed record ModerateRequest(string Decision, string? Reason);

    public sealed record CommentRequest(string Body);

    public sealed record ReactionRequest(string Reaction);
}
