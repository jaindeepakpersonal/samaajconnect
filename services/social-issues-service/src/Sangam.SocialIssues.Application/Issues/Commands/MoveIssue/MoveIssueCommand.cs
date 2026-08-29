using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;
using Sangam.SocialIssues.Domain.Issues;

namespace Sangam.SocialIssues.Application.Issues.Commands.MoveIssue;

/// <summary>
/// Moves an issue through the workflow: submit, pick up, approve, reject,
/// request changes, publish, close.
/// </summary>
/// <remarks>
/// One command for every transition rather than seven, because the transition
/// table in <see cref="SocialIssue"/> is what decides whether a move is legal
/// and there is nothing left for seven handlers to do differently. Seven would
/// mean seven copies of the tenant check, the author check and the permission
/// check, and the way that goes wrong is one of them quietly missing a check
/// the others have.
///
/// The permission is only the outer gate. Whether *this* caller may make *this*
/// move is decided against the workflow and the data: an author may withdraw
/// their own issue, and only a reviewer may approve one.
///
/// <see cref="PermissionKeys.MembersRead"/> rather than the reviewer permission,
/// because some of these moves belong to the author. The handler refuses a
/// reviewer move from somebody without SocialIssues.Approve.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record MoveIssueCommand(Guid IssueId, string Status, string? Reason)
    : ICommand<IssueDetailResponse>;

public sealed class MoveIssueCommandValidator : AbstractValidator<MoveIssueCommand>
{
    /// <summary>
    /// Moves the author is told about, and therefore has to be given a reason
    /// for. Approving needs no explanation; declining somebody's concern about
    /// their own community does.
    /// </summary>
    private static readonly IssueStatus[] NeedAReason =
        [IssueStatus.Rejected, IssueStatus.ChangesRequested];

    public MoveIssueCommandValidator()
    {
        RuleFor(x => x.IssueId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<IssueStatus>(s, ignoreCase: true, out _))
            .WithMessage(
                "Status must be one of: "
                + string.Join(", ", Enum.GetNames<IssueStatus>())
                + ".");

        RuleFor(x => x.Reason).MaximumLength(1000);

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage(
                "Say why. The member who raised this is told it, and "
                + "\"rejected\" with no explanation is not an answer.")
            .When(x => x.Status is not null
                && Enum.TryParse<IssueStatus>(x.Status, ignoreCase: true, out var parsed)
                && NeedAReason.Contains(parsed));
    }
}

public sealed class MoveIssueCommandHandler(
    IIssueRepository issues,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<MoveIssueCommandHandler> logger)
    : IRequestHandler<MoveIssueCommand, Result<IssueDetailResponse>>
{
    public async Task<Result<IssueDetailResponse>> Handle(
        MoveIssueCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<IssueDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var canReview = currentUser.HasPermission(PermissionKeys.SocialIssuesApprove);

        var issue = await issues.GetByIdAsync(command.IssueId, cancellationToken);

        if (issue is null
            || (tenantContext.TenantId is { } tenantId && issue.TenantId != tenantId))
        {
            return Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
        }

        // A draft belongs to its author until they submit it, so somebody else
        // reaching one has guessed its id.
        if (!issue.IsPublic && !issue.IsAuthor(actorId) && !canReview)
        {
            return Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
        }

        var target = Enum.Parse<IssueStatus>(command.Status, ignoreCase: true);

        if (!issue.CanMoveTo(target))
        {
            return Result.Failure<IssueDetailResponse>(Error.Conflict(
                "Issue.InvalidTransition",
                $"An issue that is {issue.Status} cannot become {target}."));
        }

        // Who may make this particular move. An author withdraws their own; a
        // reviewer decides. The workflow says which kind this is.
        var isAuthorMove = SocialIssue.IsAuthorMove(issue.Status, target);

        if (isAuthorMove ? !issue.IsAuthor(actorId) : !canReview)
        {
            return Result.Failure<IssueDetailResponse>(Error.Forbidden(
                "Issue.NotYours",
                isAuthorMove
                    ? "Only the member who raised this issue can do that."
                    : "Only a reviewer can decide about this issue."));
        }

        issue.MoveTo(target, actorId, command.Reason, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Issue {IssueId} moved to {Status} by {ActorId}", issue.Id, target, actorId);

        return Result.Success(issue.ToDetail(actorId, canReview));
    }
}
