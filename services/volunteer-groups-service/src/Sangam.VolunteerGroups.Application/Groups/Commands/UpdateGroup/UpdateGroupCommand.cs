using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.UpdateGroup;

/// <summary>
/// Corrects a group's name, description or focus area.
/// </summary>
/// <remarks>
/// A Samaaj admin's decision, same as <see cref="CreateGroup.CreateGroupCommand"/>
/// - a group's own name and description are part of how the Samaaj organises
/// itself, not something its president edits. The president, membership and
/// status each have their own command already; this touches none of them.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record UpdateGroupCommand(
    Guid GroupId,
    string Name,
    string? Description,
    string? FocusArea) : ICommand<GroupResponse>;

public sealed class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.FocusArea).MaximumLength(100);
    }
}

public sealed class UpdateGroupCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateGroupCommand, Result<GroupResponse>>
{
    public async Task<Result<GroupResponse>> Handle(
        UpdateGroupCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<GroupResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var name = command.Name.Trim();

        if (await groups.NameExistsAsync(name, group.Id, cancellationToken))
        {
            return Result.Failure<GroupResponse>(Error.Conflict(
                "Group.NameTaken", "This Samaaj already has a group with that name."));
        }

        group.Update(name, command.Description, command.FocusArea, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToResponse(actorId, currentUser.IsGroupAdmin()));
    }
}
