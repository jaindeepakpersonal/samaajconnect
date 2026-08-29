using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.DecideApplication;

/// <summary>
/// The president accepts or rejects an application, optionally giving the new
/// member a position in the same breath.
/// </summary>
/// <remarks>
/// VolunteerGroups.Lead is the outer gate and every member holds it; being
/// <i>this group's</i> president is the inner one and is checked against the
/// data here. A Samaaj admin who is not this group's president still cannot
/// decide its applications - who is in a group is the president's business, the
/// same shape as a family head deciding their own household's join requests.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record DecideApplicationCommand(
    Guid GroupId,
    Guid ApplicationId,
    bool Accept,
    string? RolePosition) : ICommand<GroupApplicationResponse>;

public sealed class DecideApplicationCommandValidator : AbstractValidator<DecideApplicationCommand>
{
    public DecideApplicationCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.RolePosition).MaximumLength(100);
    }
}

public sealed class DecideApplicationCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<DecideApplicationCommandHandler> logger)
    : IRequestHandler<DecideApplicationCommand, Result<GroupApplicationResponse>>
{
    public async Task<Result<GroupApplicationResponse>> Handle(
        DecideApplicationCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupApplicationResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<GroupApplicationResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        // "Not found", not "forbidden", and the same answer the queue gives.
        // Refusing with 403 would confirm to a non-president that this group
        // and this application both exist - and whose applications are waiting
        // is exactly what the presidency check is protecting.
        if (!group.IsPresident(actorId))
        {
            return Result.Failure<GroupApplicationResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        if (!group.DecideApplication(
            command.ApplicationId, command.Accept, actorId, command.RolePosition, clock.UtcNow))
        {
            // No pending application by that id: already decided, or never
            // existed. Either way there is nothing to decide.
            return Result.Failure<GroupApplicationResponse>(Error.NotFound(
                "Application.NotFound", "No application awaiting a decision by that id."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Application {ApplicationId} on group {GroupId} {Decision} by {ActorId}",
            command.ApplicationId,
            group.Id,
            command.Accept ? "accepted" : "rejected",
            actorId);

        return Result.Success(group.FindApplication(command.ApplicationId)!.ToResponse());
    }
}
