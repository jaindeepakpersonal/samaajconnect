using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events.Queries.GetEvent;

/// <summary>One event, from the wireframe's event-detail screen.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetEventQuery(Guid EventId) : IQuery<EventResponse>;

public sealed class GetEventQueryHandler(IEventRepository events, ICurrentUser currentUser)
    : IRequestHandler<GetEventQuery, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        GetEventQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await events.GetByIdAsync(query.EventId, cancellationToken);

        if (found is null)
        {
            return Result.Failure<EventResponse>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
        }

        // A draft is visible to whoever can publish one, and to nobody else.
        var maySee = found.Status != EventStatus.Draft
            || currentUser.HasPermission(PermissionKeys.EventsPublish);

        return maySee
            ? Result.Success(found.ToResponse(memberId))
            : Result.Failure<EventResponse>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
    }
}
