using FluentValidation;
using MediatR;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;
using Sangam.SocialIssues.Domain.Issues;

namespace Sangam.SocialIssues.Application.Issues.Commands.SubmitIssue;

/// <summary>
/// Raises an issue. The wireframe's "Submit for Approval" button sets
/// <paramref name="SubmitNow"/>; without it the issue is a draft only its
/// author can see.
/// </summary>
/// <remarks>
/// There are no attachments, and the wireframe's "Attach Evidence" button is
/// not built. SECURITY-CHECKLIST.md requires uploads to be size- and
/// type-restricted and virus-scanned before being served, the platform has no
/// file storage, and evidence attached to a social issue is exactly the kind of
/// file that would be - photographs of a place, a person, a document. Accepting
/// a link to someone else's host would put an unscanned file in front of a
/// reviewer and leak their address to that host. `IssueAttachment` and its
/// ScanStatus arrive with storage.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record SubmitIssueCommand(
    string Title,
    string Description,
    string Category,
    string? Locality,
    bool SubmitNow = true) : ICommand<IssueResponse>;

public sealed class SubmitIssueCommandValidator : AbstractValidator<SubmitIssueCommand>
{
    /// <summary>
    /// The wireframe's dropdown. A closed list rather than free text: the
    /// reviewer's queue and the published list both filter on it, and free text
    /// makes a filter that quietly misses things.
    /// </summary>
    public static readonly string[] Categories =
        ["Community", "Education", "Environment", "Health", "Safety", "Infrastructure"];

    public SubmitIssueCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.Locality).MaximumLength(150);

        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(c => Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Category must be one of: {string.Join(", ", Categories)}.");
    }
}

public sealed class SubmitIssueCommandHandler(
    IIssueRepository issues,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<SubmitIssueCommand, Result<IssueResponse>>
{
    public async Task<Result<IssueResponse>> Handle(
        SubmitIssueCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IssueResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<IssueResponse>(Error.Forbidden(
                "Issue.NoSamaaj", "Select a Samaaj before raising an issue in it."));
        }

        var issue = SocialIssue.Create(
            tenantId,
            command.Title,
            command.Description,
            command.Category,
            command.Locality,
            memberId,
            command.SubmitNow,
            clock.UtcNow);

        issues.Add(issue);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(issue.ToResponse(
            memberId, currentUser.HasPermission(PermissionKeys.SocialIssuesApprove)));
    }
}
