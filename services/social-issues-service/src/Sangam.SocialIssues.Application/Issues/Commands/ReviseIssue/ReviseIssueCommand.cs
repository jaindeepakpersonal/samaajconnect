using FluentValidation;
using MediatR;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Issues.Commands.SubmitIssue;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Commands.ReviseIssue;

/// <summary>
/// Corrects an issue that has not been decided yet, or one a reviewer sent back.
/// </summary>
/// <remarks>
/// The author's, and only the author's. Editing stops once a reviewer has
/// approved or published it: a reviewer who approved one thing and finds
/// another published has been made to endorse something they never read.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ReviseIssueCommand(
    Guid IssueId,
    string Title,
    string Description,
    string Category,
    string? Locality) : ICommand<IssueDetailResponse>;

public sealed class ReviseIssueCommandValidator : AbstractValidator<ReviseIssueCommand>
{
    public ReviseIssueCommandValidator()
    {
        RuleFor(x => x.IssueId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.Locality).MaximumLength(150);

        // The same closed list submission uses. Two lists would drift, and the
        // way they drift is a revision that cannot be saved.
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(c => SubmitIssueCommandValidator.Categories
                .Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Category must be one of: "
                + string.Join(", ", SubmitIssueCommandValidator.Categories) + ".");
    }
}

public sealed class ReviseIssueCommandHandler(
    IIssueRepository issues,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<ReviseIssueCommand, Result<IssueDetailResponse>>
{
    public async Task<Result<IssueDetailResponse>> Handle(
        ReviseIssueCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<IssueDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var issue = await issues.GetByIdAsync(command.IssueId, cancellationToken);

        if (issue is null
            || (tenantContext.TenantId is { } tenantId && issue.TenantId != tenantId)
            || !issue.IsAuthor(actorId))
        {
            // Somebody else's issue is "not found" rather than "forbidden":
            // whether a given member has raised one is theirs to say.
            return Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
        }

        if (!issue.Revise(
            command.Title, command.Description, command.Category, command.Locality, clock.UtcNow))
        {
            return Result.Failure<IssueDetailResponse>(Error.Conflict(
                "Issue.Decided",
                "This issue has already been decided and can no longer be edited."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(issue.ToDetail(
            actorId, currentUser.HasPermission(PermissionKeys.SocialIssuesApprove)));
    }
}
