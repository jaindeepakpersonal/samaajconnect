using Sangam.Boli.Domain.Auctions.Events;
using Sangam.Boli.Domain.Common;

namespace Sangam.Boli.Domain.Auctions;

/// <summary>
/// One item being bid for: a Mangal Deep, an Aarti, a Swapna.
/// </summary>
/// <remarks>
/// <b>Bids are deliberately not part of this aggregate</b>, for the reason
/// celebrity-voting-service gives about votes: a popular Boli takes hundreds of
/// bids in the last minutes before it closes, and loading them all to accept one
/// more would read the whole table on the one path that must stay fast exactly
/// when it is busiest. A <see cref="Bid"/> is written directly and the current
/// highest is a <c>MAX</c>, not a scan of a loaded collection.
///
/// <b>What this aggregate does own is the rule a bid has to satisfy</b> — the
/// window, the floor, and the increment — because those are facts about the
/// Boli and not about any one bid. <see cref="IsAcceptable"/> is that rule in one
/// place, so the handler, the screen and the tests cannot each hold their own
/// slightly different copy of it.
///
/// The rule is checked under a row lock on this Boli (see
/// <c>IBoliRepository.LockForBiddingAsync</c>) and backed by a unique index on
/// (BoliId, Amount). The lock is what makes bids on one Boli strictly ordered —
/// two people cannot both be the highest bidder, which is the domain and not a
/// limitation — and the index is what remains true even if some future code path
/// forgets to take the lock.
/// </remarks>
public sealed class Boli : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OccasionId { get; private set; }
    public Guid BoliTypeId { get; private set; }
    public string Title { get; private set; } = null!;

    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }

    /// <summary>
    /// The lowest a first bid may be.
    /// </summary>
    /// <remarks>
    /// Held in the smallest currency unit — paise — as an integer. A Boli is
    /// money, and money in a floating-point type accumulates error that shows up
    /// as a winning bid a rupee off what somebody actually offered.
    /// </remarks>
    public long StartingAmount { get; private set; }

    /// <summary>
    /// How far above the current highest the next bid must be.
    /// </summary>
    /// <remarks>
    /// The wireframe's "Minimum ₹15,600" against a ₹15,100 high. Without an
    /// increment a Boli can be won by a rupee at a time, which in a room full of
    /// people is not bidding, it is a queue.
    /// </remarks>
    public long MinIncrement { get; private set; }

    /// <summary>
    /// How long a bid in the closing seconds pushes the close out by. Zero is
    /// off, and is the default.
    /// </summary>
    /// <remarks>
    /// <b>Without this, a Boli is won by whoever arrives last rather than by
    /// whoever will pay most.</b> A bidder who waits until a second before the
    /// close and puts in one increment takes it, not because they valued it more
    /// but because nobody had time to answer. In a hall the auctioneer solves
    /// this by saying "going, going" and waiting; this is that, written down.
    ///
    /// The extension is measured from the bid, not from the old closing time.
    /// Extending <c>EndAt</c> by a fixed amount would still reward the sniper:
    /// bid at one second to go and everyone else gets one second plus the
    /// window, while bidding early costs you nothing. Measuring from <c>now</c>
    /// means every bid buys the room the *same* full window to reply, which is
    /// the property that makes sniping pointless rather than merely harder.
    ///
    /// There is no cap on how many times it can extend, and that is deliberate.
    /// A Boli that keeps extending is a Boli people are still bidding on, and
    /// ending it on a timer while hands are still up is the thing being fixed. A
    /// Samaaj that wants a hard stop closes it, which is one click and already
    /// exists.
    /// </remarks>
    public int AutoExtendSeconds { get; private set; }

    /// <summary>
    /// Who may bid, in the Samaaj's own words. Not enforced here.
    /// </summary>
    /// <remarks>
    /// Deliberately free text rather than a rule this service evaluates. Real
    /// eligibility for a Boli is things like "one per family" or "members who
    /// have completed their Paryushan pledge" — facts held in other services, or
    /// in nobody's database at all. Encoding a rule engine here would produce a
    /// language that cannot express what a Samaaj actually means and would still
    /// have to be checked by a person. It is shown to bidders and enforced by the
    /// Samaaj, and this comment exists so nobody later mistakes it for a check.
    /// </remarks>
    public string? EligibilityRule { get; private set; }

    public BoliStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    private Boli() { }   // EF Core

    public static Boli Open(
        Guid tenantId,
        Guid occasionId,
        Guid boliTypeId,
        string title,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        long startingAmount,
        long minIncrement,
        string? eligibilityRule,
        DateTimeOffset now,
        int autoExtendSeconds = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Boli
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OccasionId = occasionId,
            BoliTypeId = boliTypeId,
            Title = title.Trim(),
            StartAt = startAt,
            EndAt = endAt,
            StartingAmount = startingAmount,
            MinIncrement = minIncrement,
            AutoExtendSeconds = autoExtendSeconds < 0 ? 0 : autoExtendSeconds,
            EligibilityRule = string.IsNullOrWhiteSpace(eligibilityRule)
                ? null
                : eligibilityRule.Trim(),
            Status = BoliStatus.Scheduled,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Whether this Boli is taking bids **right now** — the status says so and
    /// the clock agrees.
    /// </summary>
    /// <remarks>
    /// Both halves matter. A Boli left at Open past its closing time is not
    /// taking bids, and a Boli whose window has arrived but which nobody has
    /// opened is not either. Callers must not derive this from the status alone;
    /// the member portal learned that lesson on celebrity voting.
    /// </remarks>
    public bool AcceptsBids(DateTimeOffset now) =>
        Status == BoliStatus.Open && now >= StartAt && now < EndAt;

    /// <summary>
    /// The lowest amount that would be accepted right now, given the highest bid
    /// so far — or the floor when there is none.
    /// </summary>
    public long MinimumNextBid(long? currentHighest) =>
        currentHighest is { } highest ? highest + MinIncrement : StartingAmount;

    /// <summary>
    /// Whether an amount clears the bar. The one copy of the rule.
    /// </summary>
    public bool IsAcceptable(long amount, long? currentHighest) =>
        amount >= MinimumNextBid(currentHighest);

    /// <summary>
    /// Pushes the close out when a bid lands inside the auto-extend window.
    /// Returns true when the window actually moved.
    /// </summary>
    /// <remarks>
    /// Called only for a bid that has already been accepted, and only under the
    /// row lock this Boli is bid on beneath — so two bidders racing in the last
    /// second cannot both read the old <c>EndAt</c> and write conflicting new
    /// ones. It is the same lock that makes "one highest bid" true, doing a
    /// second job.
    ///
    /// A bid outside the window leaves the close exactly where it was. Most
    /// bids are outside it, so most bids raise no event and change no row
    /// beyond the bid itself.
    /// </remarks>
    public bool ExtendIfClosing(DateTimeOffset now)
    {
        if (AutoExtendSeconds <= 0)
        {
            return false;
        }

        var window = TimeSpan.FromSeconds(AutoExtendSeconds);

        if (EndAt - now > window)
        {
            return false;
        }

        var previous = EndAt;

        EndAt = now + window;

        Raise(new BoliExtendedDomainEvent(Id, TenantId, OccasionId, previous, EndAt, now));

        return true;
    }

    /// <summary>Starts the bidding window. False when it is not Scheduled.</summary>
    public bool Start()
    {
        if (Status == BoliStatus.Open)
        {
            return true;
        }

        if (Status != BoliStatus.Scheduled)
        {
            return false;
        }

        Status = BoliStatus.Open;

        return true;
    }

    /// <summary>
    /// Closes the bidding. Idempotent: closing a closed Boli is not an error,
    /// because the close is going to be raced by a clock somewhere.
    /// </summary>
    public bool Close(DateTimeOffset now)
    {
        if (Status is BoliStatus.Closed or BoliStatus.ResultPublished)
        {
            return true;
        }

        if (Status != BoliStatus.Open)
        {
            return false;
        }

        Status = BoliStatus.Closed;
        ClosedAt = now;

        Raise(new BoliClosedDomainEvent(Id, TenantId, OccasionId, now));

        return true;
    }

    /// <summary>
    /// Marks the result published. Only from Closed, and only once.
    /// </summary>
    /// <remarks>
    /// Publishing is irreversible through this API by design (SERVICES.md).
    /// A correction is a different, audited act — not a second publish — because
    /// a result the Samaaj has been told can be quietly changed is not a result.
    ///
    /// The winner and the amount are passed in rather than read off this object:
    /// a <see cref="BoliResult"/> is not part of this aggregate, and the raise has
    /// to happen here because only an <see cref="AggregateRoot"/> can raise.
    /// Handing them in keeps the event complete without pretending the Boli holds
    /// its own result.
    /// </remarks>
    public bool MarkPublished(Guid winningMemberId, long amount, DateTimeOffset now)
    {
        if (Status != BoliStatus.Closed)
        {
            return false;
        }

        Status = BoliStatus.ResultPublished;

        Raise(new BoliResultPublishedDomainEvent(
            Id, TenantId, OccasionId, winningMemberId, amount, now));

        return true;
    }
}

public enum BoliStatus
{
    /// <summary>Created, with a window, but not taking bids.</summary>
    Scheduled = 1,

    Open = 2,
    Closed = 3,
    ResultPublished = 4,
}
