using MediatR;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Application.Security;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Auctions.Queries;

// ---- Occasions ---------------------------------------------------------------

/// <summary>Every occasion this Samaaj has held or announced, newest first.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListOccasionsQuery : IQuery<IReadOnlyList<OccasionResponse>>;

public sealed class ListOccasionsQueryHandler(
    IOccasionRepository occasions, IBoliRepository boli)
    : IRequestHandler<ListOccasionsQuery, Result<IReadOnlyList<OccasionResponse>>>
{
    public async Task<Result<IReadOnlyList<OccasionResponse>>> Handle(
        ListOccasionsQuery query, CancellationToken cancellationToken)
    {
        var all = await occasions.ListAsync(cancellationToken);
        var responses = new List<OccasionResponse>(all.Count);

        foreach (var occasion in all)
        {
            var lots = await boli.ListForOccasionAsync(occasion.Id, cancellationToken);

            responses.Add(BoliMappings.ToResponse(occasion, lots.Count));
        }

        return Result.Success<IReadOnlyList<OccasionResponse>>(responses);
    }
}

/// <summary>One occasion, its types, and the Boli under it.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetOccasionQuery(Guid OccasionId) : IQuery<OccasionDetailResponse>;

public sealed class GetOccasionQueryHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetOccasionQuery, Result<OccasionDetailResponse>>
{
    public async Task<Result<OccasionDetailResponse>> Handle(
        GetOccasionQuery query, CancellationToken cancellationToken)
    {
        var occasion = await occasions.GetByIdAsync(query.OccasionId, cancellationToken);

        if (occasion is null)
        {
            return Result.Failure<OccasionDetailResponse>(
                Error.NotFound("Occasion.NotFound", "No such occasion in this Samaaj."));
        }

        var lots = await boli.ListForOccasionAsync(occasion.Id, cancellationToken);
        var now = clock.UtcNow;
        var me = currentUser.UserId;

        var described = new List<BoliResponse>(lots.Count);

        foreach (var lot in lots)
        {
            var bids = await boli.ListBidsAsync(lot.Id, cancellationToken);
            var top = bids.Count > 0 ? bids[0] : null;

            described.Add(BoliMappings.ToResponse(
                lot,
                occasion.FindType(lot.BoliTypeId)?.Name ?? string.Empty,
                now,
                top?.Amount,
                top is not null && top.MemberId == me,
                bids.Count));
        }

        return Result.Success(new OccasionDetailResponse(
            occasion.Id,
            occasion.Title,
            occasion.Description,
            occasion.OccasionDate,
            occasion.Status.ToString(),
            [.. occasion.Types.Select(BoliMappings.ToResponse)],
            described));
    }
}

// ---- Boli --------------------------------------------------------------------

/// <summary>Every Boli currently taking bids. The wireframe's "Active Boli".</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetActiveBoliQuery : IQuery<IReadOnlyList<BoliResponse>>;

public sealed class GetActiveBoliQueryHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetActiveBoliQuery, Result<IReadOnlyList<BoliResponse>>>
{
    public async Task<Result<IReadOnlyList<BoliResponse>>> Handle(
        GetActiveBoliQuery query, CancellationToken cancellationToken)
    {
        var open = await boli.ListOpenAsync(cancellationToken);
        var now = clock.UtcNow;
        var me = currentUser.UserId;

        var described = new List<BoliResponse>(open.Count);

        foreach (var lot in open)
        {
            // Status Open is not the same as taking bids: the window has to have
            // arrived and not passed. Filtering here rather than in SQL keeps the
            // one definition of "open now" on the aggregate.
            if (!lot.AcceptsBids(now))
            {
                continue;
            }

            var occasion = await occasions.GetByIdAsync(lot.OccasionId, cancellationToken);
            var bids = await boli.ListBidsAsync(lot.Id, cancellationToken);
            var top = bids.Count > 0 ? bids[0] : null;

            described.Add(BoliMappings.ToResponse(
                lot,
                occasion?.FindType(lot.BoliTypeId)?.Name ?? string.Empty,
                now,
                top?.Amount,
                top is not null && top.MemberId == me,
                bids.Count));
        }

        return Result.Success<IReadOnlyList<BoliResponse>>(described);
    }
}

/// <summary>One Boli, with its live highest and the minimum next bid.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBoliQuery(Guid BoliId) : IQuery<BoliResponse>;

public sealed class GetBoliQueryHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetBoliQuery, Result<BoliResponse>>
{
    public async Task<Result<BoliResponse>> Handle(
        GetBoliQuery query, CancellationToken cancellationToken)
    {
        var lot = await boli.GetByIdAsync(query.BoliId, cancellationToken);

        if (lot is null)
        {
            return Result.Failure<BoliResponse>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        return await BoliMappings.DescribeAsync(
            lot, occasions, boli, currentUser.UserId, clock.UtcNow, cancellationToken);
    }
}

/// <summary>
/// The bid history: amounts and times, and which are the reader's own.
/// </summary>
/// <remarks>
/// Never member ids. See <see cref="BoliMappings"/>.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBidHistoryQuery(Guid BoliId) : IQuery<IReadOnlyList<BidResponse>>;

public sealed class GetBidHistoryQueryHandler(
    IBoliRepository boli, ICurrentUser currentUser)
    : IRequestHandler<GetBidHistoryQuery, Result<IReadOnlyList<BidResponse>>>
{
    public async Task<Result<IReadOnlyList<BidResponse>>> Handle(
        GetBidHistoryQuery query, CancellationToken cancellationToken)
    {
        var lot = await boli.GetByIdAsync(query.BoliId, cancellationToken);

        if (lot is null)
        {
            return Result.Failure<IReadOnlyList<BidResponse>>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        var bids = await boli.ListBidsAsync(query.BoliId, cancellationToken);
        var me = currentUser.UserId;

        return Result.Success<IReadOnlyList<BidResponse>>(
            [.. bids.Select(bid => BoliMappings.ToResponse(bid, me))]);
    }
}

// ---- Results -----------------------------------------------------------------

/// <summary>
/// One Boli's result.
/// </summary>
/// <remarks>
/// Answers 404 until a result has been recorded, and carries no winner until it
/// has been published — so an unpublished result tells a member the Boli is
/// settled without telling them who won, which is the state the two-step is for.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBoliResultQuery(Guid BoliId) : IQuery<BoliResultResponse>;

public sealed class GetBoliResultQueryHandler(
    IBoliRepository boli, ICurrentUser currentUser)
    : IRequestHandler<GetBoliResultQuery, Result<BoliResultResponse>>
{
    public async Task<Result<BoliResultResponse>> Handle(
        GetBoliResultQuery query, CancellationToken cancellationToken)
    {
        var lot = await boli.GetByIdAsync(query.BoliId, cancellationToken);

        if (lot is null)
        {
            return Result.Failure<BoliResultResponse>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        var result = await boli.GetResultAsync(lot.Id, cancellationToken);

        if (result is null)
        {
            return Result.Failure<BoliResultResponse>(
                Error.NotFound("Boli.NoResult", "No result has been recorded for this Boli."));
        }

        return Result.Success(BoliMappings.ToResponse(result, lot, currentUser.UserId));
    }
}

/// <summary>Everything this Samaaj has announced, newest first.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetPublishedResultsQuery : IQuery<IReadOnlyList<BoliResultResponse>>;

public sealed class GetPublishedResultsQueryHandler(
    IBoliRepository boli, ICurrentUser currentUser)
    : IRequestHandler<GetPublishedResultsQuery, Result<IReadOnlyList<BoliResultResponse>>>
{
    public async Task<Result<IReadOnlyList<BoliResultResponse>>> Handle(
        GetPublishedResultsQuery query, CancellationToken cancellationToken)
    {
        var results = await boli.ListPublishedResultsAsync(cancellationToken);
        var me = currentUser.UserId;
        var responses = new List<BoliResultResponse>(results.Count);

        foreach (var result in results)
        {
            var lot = await boli.GetByIdAsync(result.BoliId, cancellationToken);

            if (lot is not null)
            {
                responses.Add(BoliMappings.ToResponse(result, lot, me));
            }
        }

        return Result.Success<IReadOnlyList<BoliResultResponse>>(responses);
    }
}
