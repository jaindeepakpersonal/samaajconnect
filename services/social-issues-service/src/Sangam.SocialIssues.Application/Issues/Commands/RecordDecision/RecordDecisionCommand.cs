using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Commands.RecordDecision;

/// <summary>
/// The Samaaj Panch decides an issue that was put in front of it, and closes it.
/// </summary>
/// <remarks>
/// <b>Any Panch member may record it.</b> There is no quorum and no chair: the
/// Panch discusses an issue offline, and one of its members writes down what it
/// decided. Who wrote it down is on the issue and in the history.
///
/// <paramref name="MonitoringGroupId"/> hands the follow-up to a volunteer group
/// when the Panch wants one - "if required", so it is optional. It is a string on
/// the wire and parsed here: a Guid-typed wire field makes ASP.NET Core's own
/// model binding answer an unparseable value with an unhandled 400 before any
/// validator runs. It is <i>not</i> checked against volunteer-groups-service - a
/// synchronous call to confirm the group exists would couple the two - so the
/// portal offers only real groups and the id is otherwise trusted.
///
/// <see cref="PermissionKeys.MembersRead"/> is only the outer gate. The Panch
/// members are ordinary members, so whether this caller may decide is the
/// roster's answer - see <see cref="ReviewAccessResolver"/>.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RecordDecisionCommand(Guid IssueId, string Decision, string? MonitoringGroupId)
    : ICommand<IssueDetailResponse>;

public sealed class RecordDecisionCommandValidator : AbstractValidator<RecordDecisionCommand>
{
    public RecordDecisionCommandValidator()
    {
        RuleFor(x => x.IssueId).NotEmpty();

        RuleFor(x => x.Decision)
            .NotEmpty()
            .WithMessage("Say what the Panch decided. An issue is not closed without one.")
            .MaximumLength(2000);

        RuleFor(x => x.MonitoringGroupId)
            .Must(id => Guid.TryParse(id, out var parsed) && parsed != Guid.Empty)
            .WithMessage("MonitoringGroupId must be a valid group id.")
            .When(x => !string.IsNullOrWhiteSpace(x.MonitoringGroupId));
    }
}

public sealed class RecordDecisionCommandHandler(
    IIssueRepository issues,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ReviewAccessResolver reviewAccess,
    IDateTimeProvider clock,
    ILogger<RecordDecisionCommandHandler> logger)
    : IRequestHandler<RecordDecisionCommand, Result<IssueDetailResponse>>
{
    public async Task<Result<IssueDetailResponse>> Handle(
        RecordDecisionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<IssueDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var access = await reviewAccess.ResolveAsync(cancellationToken);
        var issue = await issues.GetByIdAsync(command.IssueId, cancellationToken);

        if (issue is null
            || (tenantContext.TenantId is { } tenantId && issue.TenantId != tenantId))
        {
            return Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
        }

        // Somebody who could not have seen this issue is told it does not exist.
        if (!issue.IsPublic && !issue.IsAuthor(actorId) && !access.MaySee(issue))
        {
            return Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
        }

        if (!access.MayAct(issue))
        {
            return Result.Failure<IssueDetailResponse>(Error.Forbidden(
                "Issue.NotOnPanch",
                "Only a member of the Samaaj Panch can record its decision."));
        }

        Guid? monitoringGroupId = string.IsNullOrWhiteSpace(command.MonitoringGroupId)
            ? null
            : Guid.Parse(command.MonitoringGroupId);

        if (!issue.RecordDecision(command.Decision, monitoringGroupId, actorId, clock.UtcNow))
        {
            return Result.Failure<IssueDetailResponse>(Error.Conflict(
                "Issue.NotDecisionPending",
                $"An issue that is {issue.Status} is not waiting on a decision."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Issue {IssueId} decided by {ActorId}, monitoring handed to a group: {HandedOver}",
            issue.Id, actorId, monitoringGroupId is not null);

        return Result.Success(issue.ToDetail(actorId, access));
    }
}
