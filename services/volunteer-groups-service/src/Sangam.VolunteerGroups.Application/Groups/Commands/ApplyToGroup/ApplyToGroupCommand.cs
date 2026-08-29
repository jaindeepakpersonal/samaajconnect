using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.ApplyToGroup;

/// <summary>Asks to join a group. The president decides.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ApplyToGroupCommand(Guid GroupId, string? Note)
    : ICommand<ApplyToGroupResponse>;

/// <summary>
/// <paramref name="Applied"/> is false when there was nothing to do - already a
/// member, or already waiting. Reported as success: applying twice should look
/// like applying once, and the member's own status is on the group response
/// either way.
/// </summary>
public sealed record ApplyToGroupResponse(Guid GroupId, bool Applied, string Status);

public sealed class ApplyToGroupCommandValidator : AbstractValidator<ApplyToGroupCommand>
{
    public ApplyToGroupCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class ApplyToGroupCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<ApplyToGroupCommand, Result<ApplyToGroupResponse>>
{
    public async Task<Result<ApplyToGroupResponse>> Handle(
        ApplyToGroupCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ApplyToGroupResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<ApplyToGroupResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var application = group.Apply(memberId, command.Note, clock.UtcNow);

        if (application is null)
        {
            var status = group.HasMember(memberId) ? "AlreadyAMember" : "Pending";

            return Result.Success(new ApplyToGroupResponse(group.Id, false, status));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(
            new ApplyToGroupResponse(group.Id, true, application.Status.ToString()));
    }
}
