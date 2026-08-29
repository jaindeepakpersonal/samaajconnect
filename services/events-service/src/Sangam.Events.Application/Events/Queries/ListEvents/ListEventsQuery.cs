using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;

namespace Sangam.Events.Application.Events.Queries.ListEvents;

/// <summary>
/// The Samaaj's events, soonest first, each with the asking member's own
/// standing - which is what the wireframe's status pill and RSVP button read.
/// </summary>
/// <remarks>
/// <paramref name="IncludeDrafts"/> is refused to anyone without
/// Events.Publish. A draft is an event nobody has been told about, and a member
/// seeing one would be seeing something that may never happen.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListEventsQuery(bool IncludeDrafts = false, bool IncludePast = false)
    : IQuery<IReadOnlyList<EventResponse>>;

public sealed class ListEventsQueryHandler(
    IEventRepository events,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListEventsQuery, Result<IReadOnlyList<EventResponse>>>
{
    public async Task<Result<IReadOnlyList<EventResponse>>> Handle(
        ListEventsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<EventResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // Asking for drafts without the permission quietly gets the published
        // list rather than a 403. Refusing would tell the asker that drafts
        // exist, and the honest answer to "show me everything" from a member is
        // "here is everything you can see".
        var includeDrafts =
            query.IncludeDrafts && currentUser.HasPermission(PermissionKeys.EventsPublish);

        var found = await events.ListAsync(
            publishedOnly: !includeDrafts,
            upcomingOnly: !query.IncludePast,
            clock.UtcNow,
            cancellationToken);

        IReadOnlyList<EventResponse> results = [.. found.Select(e => e.ToResponse(memberId))];

        return Result.Success(results);
    }
}
