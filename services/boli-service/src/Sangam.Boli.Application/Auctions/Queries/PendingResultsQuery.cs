using MediatR;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Application.Security;

namespace Sangam.Boli.Application.Auctions.Queries;

/// <summary>
/// Results recorded and waiting to be announced — the publisher's work queue.
/// </summary>
/// <remarks>
/// <para>
/// <b>The wireframe's "Results Awaiting Publication" card, which nothing could
/// answer.</b> Recording a result and announcing it are separate acts on
/// purpose, so a result can sit between them; but the only read that reached one
/// was <c>GetBoliResultQuery</c>, which needs the Boli id you are looking for.
/// Finding what is waiting meant walking every occasion, then every Boli under
/// it, then asking each for a result — which no screen was ever going to do, so
/// no screen did, and the middle state of the platform's most deliberate
/// two-step workflow was invisible.
/// </para>
/// <para>
/// <b>Gated on <c>Boli.PublishResults</c>, not <c>Boli.Manage</c>.</b> This is
/// the list of things that person is being asked to act on. The separation
/// between the two permissions currently separates nobody — both are granted to
/// the same roles — but a Samaaj that wants a second pair of eyes on
/// announcements should get a queue that belongs to the eyes, not to the hands.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.BoliPublishResults)]
public sealed record ListPendingResultsQuery : IQuery<IReadOnlyList<PendingResultResponse>>;

/// <summary>
/// One recorded, unannounced result.
/// </summary>
/// <remarks>
/// <para>
/// <b>No winner, and that is deliberate even here.</b> The wireframe's publish
/// screen draws "Winning Bid: ₹18,400 — Member ID 1042", and this answers with
/// the amount and not the member. <c>BoliResultResponse</c> holds
/// <c>WinningMemberId</c> null until <c>PublishedAt</c> is not, for everybody
/// including the manager who recorded it, and adding a second shape that carries
/// the winner early would undo that invariant rather than sit beside it — "one
/// record names the winner, and only after publication" is far easier to keep
/// true than "two records name the winner, one of them only to the right
/// caller".
/// </para>
/// <para>
/// Nothing is lost by it. The winner is not something the publisher chooses or
/// could get wrong: <c>RecordResultCommand</c> reads the highest bid and the
/// winner is not a parameter. The amount is what identifies that bid, and the
/// amount is here.
/// </para>
/// <para>
/// <b><c>RecordedBy</c> is here and is not on the member-facing shape.</b> Who
/// recorded a result is the publisher's business — it is half of the second pair
/// of eyes — and nobody else's. Putting it on <c>BoliResultResponse</c> would
/// have announced the manager's id to every member of the Samaaj alongside every
/// published result.
/// </para>
/// </remarks>
public sealed record PendingResultResponse(
    Guid BoliId,
    string BoliTitle,
    Guid OccasionId,
    long Amount,
    Guid RecordedBy,
    DateTimeOffset RecordedAt);

public sealed class ListPendingResultsQueryHandler(IBoliRepository boli)
    : IRequestHandler<ListPendingResultsQuery, Result<IReadOnlyList<PendingResultResponse>>>
{
    public async Task<Result<IReadOnlyList<PendingResultResponse>>> Handle(
        ListPendingResultsQuery query, CancellationToken cancellationToken)
    {
        var pending = await boli.ListUnpublishedResultsAsync(cancellationToken);
        var responses = new List<PendingResultResponse>(pending.Count);

        foreach (var result in pending)
        {
            var lot = await boli.GetByIdAsync(result.BoliId, cancellationToken);

            // A result whose Boli cannot be read is skipped rather than
            // answered with a blank title, the same as the published list does.
            if (lot is null)
            {
                continue;
            }

            responses.Add(new PendingResultResponse(
                result.BoliId,
                lot.Title,
                lot.OccasionId,
                result.Amount,
                result.RecordedBy,
                result.RecordedAt));
        }

        return Result.Success<IReadOnlyList<PendingResultResponse>>(responses);
    }
}
