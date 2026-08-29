using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.CreateGroup;

/// <summary>
/// Creates a volunteer group and names its president.
/// </summary>
/// <remarks>
/// A Samaaj admin's decision, not a member's: a group is part of how a Samaaj
/// organises itself. Who joins it afterwards is the president's business, which
/// is the split this whole service is built around.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record CreateGroupCommand(
    string Name,
    string? Description,
    string? FocusArea,
    Guid PresidentMemberId) : ICommand<GroupResponse>;

public sealed class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.FocusArea).MaximumLength(100);

        RuleFor(x => x.PresidentMemberId)
            .NotEmpty()
            .WithMessage("Name a president. A group without one has nobody to decide who joins it.");
    }
}

public sealed class CreateGroupCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateGroupCommand, Result<GroupResponse>>
{
    public async Task<Result<GroupResponse>> Handle(
        CreateGroupCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<GroupResponse>(Error.Forbidden(
                "Group.NoSamaaj", "Select a Samaaj before creating a group in it."));
        }

        if (await groups.NameExistsAsync(command.Name.Trim(), cancellationToken))
        {
            // Two groups with the same name in one Samaaj is a support ticket
            // waiting to happen - nobody can tell which one they applied to.
            return Result.Failure<GroupResponse>(Error.Conflict(
                "Group.NameTaken", "This Samaaj already has a group with that name."));
        }

        var group = VolunteerGroup.Create(
            tenantId,
            command.Name,
            command.Description,
            command.FocusArea,
            command.PresidentMemberId,
            clock.UtcNow);

        groups.Add(group);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToResponse(actorId));
    }
}
