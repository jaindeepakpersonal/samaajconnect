using FluentValidation;
using MediatR;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Application.Common;
using Sangam.Timeline.Application.Security;

namespace Sangam.Timeline.Application.Posts.Queries.GetFeed;

/// <summary>
/// The Samaaj's timeline: approved posts, plus this member's own posts whatever
/// their status.
/// </summary>
/// <remarks>
/// The member-portal wireframe shows both in one list - an approved member post
/// alongside "Your Post • Pending Review" - and that is right: a member who
/// posts and then cannot see it anywhere reasonably concludes it was lost.
/// Their own pending and rejected posts are visible to them and to nobody else.
/// </remarks>
[RequiresPermission(PermissionKeys.TimelinePost)]
public sealed record GetFeedQuery(int? Limit) : IQuery<IReadOnlyList<PostResponse>>;

public sealed class GetFeedQueryValidator : AbstractValidator<GetFeedQuery>
{
    public GetFeedQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100).When(x => x.Limit.HasValue);
    }
}

public sealed class GetFeedQueryHandler(IPostRepository posts, ICurrentUser currentUser)
    : IRequestHandler<GetFeedQuery, Result<IReadOnlyList<PostResponse>>>
{
    private const int DefaultLimit = 30;

    public async Task<Result<IReadOnlyList<PostResponse>>> Handle(
        GetFeedQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<PostResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var limit = query.Limit ?? DefaultLimit;

        var approved = await posts.ListFeedAsync(limit, cancellationToken);
        var mine = await posts.ListForAuthorAsync(memberId, limit, cancellationToken);

        // Two queries rather than one that knows who is asking. Merged here
        // because an approved post by this member comes back from both.
        IReadOnlyList<PostResponse> feed =
        [
            .. approved
                .Concat(mine)
                .DistinctBy(p => p.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Take(limit)
                .Select(p => p.ToResponse(memberId))
        ];

        return Result.Success(feed);
    }
}
