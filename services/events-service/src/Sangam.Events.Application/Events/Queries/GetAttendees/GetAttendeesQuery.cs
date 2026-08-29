using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;

namespace Sangam.Events.Application.Events.Queries.GetAttendees;

/// <summary>
/// Who is coming, and who is waiting.
/// </summary>
/// <remarks>
/// The organiser's list, not a member's. Who else is going to an event is a
/// fact about other people, and a Samaaj is a place where that matters - so
/// this needs Events.Publish rather than the Members.Read every member holds.
/// </remarks>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record GetAttendeesQuery(Guid EventId) : IQuery<IReadOnlyList<AttendeeResponse>>;

public sealed class GetAttendeesQueryHandler(IEventRepository events)
    : IRequestHandler<GetAttendeesQuery, Result<IReadOnlyList<AttendeeResponse>>>
{
    public async Task<Result<IReadOnlyList<AttendeeResponse>>> Handle(
        GetAttendeesQuery query,
        CancellationToken cancellationToken)
    {
        var found = await events.GetByIdAsync(query.EventId, cancellationToken);

        return found is null
            ? Result.Failure<IReadOnlyList<AttendeeResponse>>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."))
            : Result.Success(found.ToAttendees());
    }
}
