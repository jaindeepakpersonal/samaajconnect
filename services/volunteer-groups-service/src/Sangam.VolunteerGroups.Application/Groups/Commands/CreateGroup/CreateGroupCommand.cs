using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.CreateGroup;

/// <summary>
/// Creates a volunteer group, optionally naming its president.
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
    string? PresidentMemberId,

    /// <summary>
    /// The locality the group serves, optional. An Area Admin's groups are
    /// always in their own locality: omitted, it is filled in; any other, refused.
    /// </summary>
    Guid? LocalityId = null) : ICommand<GroupResponse>;

public sealed class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.FocusArea).MaximumLength(100);

        // Optional: a group may be created before anyone is chosen to lead it.
        // A string on the wire, parsed only in the handler, so a malformed id
        // fails here rather than crashing the JSON binder before validation runs.
        RuleFor(x => x.PresidentMemberId)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || (Guid.TryParse(value, out var id) && id != Guid.Empty))
            .WithMessage("PresidentMemberId must be a valid member id, or left empty.");
    }
}

public sealed class CreateGroupCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
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

        if (await groups.NameExistsAsync(command.Name.Trim(), cancellationToken: cancellationToken))
        {
            // Two groups with the same name in one Samaaj is a support ticket
            // waiting to happen - nobody can tell which one they applied to.
            return Result.Failure<GroupResponse>(Error.Conflict(
                "Group.NameTaken", "This Samaaj already has a group with that name."));
        }

        // An Area Admin sets groups up in their own locality and nowhere else.
        var localityId = command.LocalityId ?? areaScope.AreaLocalityId;

        if (!areaScope.MayAdminister(localityId))
        {
            return Result.Failure<GroupResponse>(Error.Forbidden(
                "Group.OutsideLocality", "You can only set up groups in your own locality."));
        }

        var group = VolunteerGroup.Create(
            tenantId,
            command.Name,
            command.Description,
            command.FocusArea,
            string.IsNullOrWhiteSpace(command.PresidentMemberId) ? null : Guid.Parse(command.PresidentMemberId),
            clock.UtcNow,
            localityId);

        groups.Add(group);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToResponse(actorId, currentUser.Administers(areaScope, group)));
    }
}
