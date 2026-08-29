using MediatR;
using Sangam.SocialIssues.Api.Extensions;
using Sangam.SocialIssues.Application.Issues;
using Sangam.SocialIssues.Application.Issues.Commands.MoveIssue;
using Sangam.SocialIssues.Application.Issues.Commands.ReviseIssue;
using Sangam.SocialIssues.Application.Issues.Commands.SubmitIssue;
using Sangam.SocialIssues.Application.Issues.Queries.GetApprovalQueue;
using Sangam.SocialIssues.Application.Issues.Queries.GetIssue;
using Sangam.SocialIssues.Application.Issues.Queries.ListIssues;

namespace Sangam.SocialIssues.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class IssueEndpoints
{
    public static IEndpointRouteBuilder MapIssueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/social-issues").WithTags("Social issues");

        group.MapGet("/", async (
                string? category,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListIssuesQuery(category), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListIssues")
            .WithSummary("Published issues, plus this member's own whatever their status.")
            .Produces<IReadOnlyList<IssueResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Before "/{id}", or a request for the queue would be read as an issue
        // whose id is "approval-queue".
        group.MapGet("/approval-queue", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetApprovalQueueQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetApprovalQueue")
            .WithSummary("What a reviewer has to decide about, oldest first.")
            .Produces<IReadOnlyList<IssueResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (
                SubmitIssueRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new SubmitIssueCommand(
                    request.Title,
                    request.Description,
                    request.Category,
                    request.Locality,
                    request.SubmitNow);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(issue =>
                    Results.Created($"/v1/social-issues/{issue.Id}", issue));
            })
            .RequireAuthorization()
            .WithName("SubmitIssue")
            .WithSummary("Raise an issue. Submitted by default; save a draft with submitNow=false.")
            .Produces<IssueResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetIssueQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetIssue")
            .WithSummary("One issue, with the record of how it got where it is.")
            .Produces<IssueDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (
                Guid id,
                ReviseIssueRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new ReviseIssueCommand(
                    id, request.Title, request.Description, request.Category, request.Locality);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ReviseIssue")
            .WithSummary("Correct an issue that has not been decided, or one sent back for changes.")
            .Produces<IssueDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/status", async (
                Guid id,
                MoveRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MoveIssueCommand(id, request.Status, request.Reason), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MoveIssue")
            .WithSummary(
                "Move the issue through the workflow. One endpoint for every transition; "
                + "which are legal for this caller is on the issue as availableTransitions.")
            .Produces<IssueDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// <paramref name="SubmitNow"/> is the wireframe's "Submit for Approval".
    /// False saves a draft only the author can see.
    /// </summary>
    public sealed record SubmitIssueRequest(
        string Title,
        string Description,
        string Category,
        string? Locality,
        bool SubmitNow = true);

    public sealed record ReviseIssueRequest(
        string Title,
        string Description,
        string Category,
        string? Locality);

    /// <summary>
    /// <paramref name="Status"/> is required and has no default: a workflow
    /// endpoint whose safest value is implicit is one where a mistyped request
    /// quietly publishes something.
    /// </summary>
    public sealed record MoveRequest(string Status, string? Reason);
}
