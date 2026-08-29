using FluentValidation;
using MediatR;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;

namespace Sangam.Timeline.Application.Posts.Queries.GetModerationQueue;

/// <summary>
/// What a moderator has to look at: posts awaiting review, and approved posts
/// members have reported.
/// </summary>
/// <remarks>
/// Reported posts belong in the same queue as new ones. A separate "reports"
/// screen is a screen somebody has to remember to open, and the whole point of
/// a report is that it should not wait for that.
/// </remarks>
[RequiresPermission(PermissionKeys.TimelineModerate)]
public sealed record GetModerationQueueQuery(int? Limit) : IQuery<IReadOnlyList<ModerationQueueItem>>;

public sealed class GetModerationQueueQueryValidator : AbstractValidator<GetModerationQueueQuery>
{
    public GetModerationQueueQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 200).When(x => x.Limit.HasValue);
    }
}

public sealed class GetModerationQueueQueryHandler(IPostRepository posts)
    : IRequestHandler<GetModerationQueueQuery, Result<IReadOnlyList<ModerationQueueItem>>>
{
    private const int DefaultLimit = 50;

    public async Task<Result<IReadOnlyList<ModerationQueueItem>>> Handle(
        GetModerationQueueQuery query,
        CancellationToken cancellationToken)
    {
        var queue = await posts.ListModerationQueueAsync(
            query.Limit ?? DefaultLimit, cancellationToken);

        IReadOnlyList<ModerationQueueItem> items = [.. queue.Select(p => p.ToQueueItem())];

        return Result.Success(items);
    }
}
