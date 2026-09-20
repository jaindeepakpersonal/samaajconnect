using Sangam.SocialIssues.Domain.Common;

namespace Sangam.SocialIssues.Domain.Issues;

/// <summary>
/// Something a member wants the Samaaj to do something about: road safety near
/// the school, support for elderly members, lighting in the community park.
/// </summary>
/// <remarks>
/// This is the platform's longest-lived workflow — seven states — and the
/// member-portal shows it as a progress strip: Submitted → Under Review →
/// Decision pending → Closed. The transitions are declared as a table in
/// <see cref="Transitions"/> rather than scattered through methods, because
/// seven states have dozens of plausible transitions and the only way to see
/// which are allowed is to have them written down in one place.
///
/// <b>The Samaaj Panch judges it.</b> A reviewer's approval puts an issue in
/// <see cref="IssueStatus.DecisionPending"/>: it is now discussed by the Panch,
/// which records a <see cref="Decision"/> - optionally handing the monitoring
/// to a volunteer group - and closes it. That last move is not in the table:
/// it needs a decision text and a monitoring group, so it has its own method,
/// <see cref="RecordDecision"/>, and its own command.
///
/// <b>Every transition is recorded.</b> <see cref="IssueStatusHistory"/> is
/// append-only within the aggregate: nothing changes or removes a row. A member
/// whose issue was rejected will ask why, and a Samaaj that cannot answer has
/// failed them twice.
///
/// The subtitle on the member's screen — "member submissions are shown only
/// after valid approval" — is the invariant this aggregate exists to hold. An
/// issue is visible to the Samaaj only once a reviewer has put it in front of
/// the Panch, or the Panch has decided it.
/// </remarks>
public sealed class SocialIssue : AggregateRoot, ITenantScopedEntity
{
    /// <summary>
    /// Which moves are legal, and who may make them.
    /// </summary>
    /// <remarks>
    /// Read this as the whole workflow. A move not in this table cannot happen,
    /// which is why adding a state means adding rows here rather than an `if`
    /// somewhere.
    ///
    /// <c>ByAuthor</c> means the member who raised it; anything else is a
    /// reviewer holding SocialIssues.Approve. Both are checked in the handler
    /// against the data, not by a role claim alone.
    /// </remarks>
    private static readonly (IssueStatus From, IssueStatus To, bool ByAuthor)[] Transitions =
    [
        // The member's own moves.
        (IssueStatus.Draft, IssueStatus.Submitted, true),
        (IssueStatus.ChangesRequested, IssueStatus.Submitted, true),

        // A member may withdraw their own issue right up until it is published.
        // After that the Samaaj has been told about it, and taking it back is
        // the Samaaj's decision rather than theirs.
        (IssueStatus.Draft, IssueStatus.Closed, true),
        (IssueStatus.Submitted, IssueStatus.Closed, true),
        (IssueStatus.ChangesRequested, IssueStatus.Closed, true),

        // The reviewer's moves.
        (IssueStatus.Submitted, IssueStatus.UnderReview, false),
        (IssueStatus.UnderReview, IssueStatus.DecisionPending, false),
        (IssueStatus.UnderReview, IssueStatus.Rejected, false),
        (IssueStatus.UnderReview, IssueStatus.ChangesRequested, false),

        // Deciding straight from Submitted, without picking it up first. The
        // wireframe's queue offers Approve, Reject and Request Changes on a
        // submitted issue, so requiring a separate "start review" click would
        // be a step the design does not have.
        (IssueStatus.Submitted, IssueStatus.DecisionPending, false),
        (IssueStatus.Submitted, IssueStatus.Rejected, false),
        (IssueStatus.Submitted, IssueStatus.ChangesRequested, false),

        // A rejected issue is already over.
        (IssueStatus.Rejected, IssueStatus.Closed, false),

        // DecisionPending -> Closed is deliberately NOT a row. It needs the
        // Panch's decision text, so the only way there is RecordDecision; a
        // plain status move that closed it would leave a "decided" issue with
        // no decision.
    ];

    private readonly List<IssueStatusHistory> _history = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string Category { get; private set; } = null!;

    /// <summary>Where in the Samaaj's area this is. The wireframe shows it on the queue card.</summary>
    public string? Locality { get; private set; }

    public Guid SubmittedByMemberId { get; private set; }
    public IssueStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>What the Panch decided. Null until it has.</summary>
    public string? Decision { get; private set; }

    /// <summary>
    /// The volunteer group the Panch handed the monitoring to, when it did. A
    /// loose id: volunteer-groups-service owns groups, and a synchronous call
    /// to check one would couple the two.
    /// </summary>
    public Guid? MonitoringGroupId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>The Panch member who recorded the decision.</summary>
    public Guid? DecidedBy { get; private set; }

    public IReadOnlyCollection<IssueStatusHistory> History => _history.AsReadOnly();

    private SocialIssue() { }   // EF Core

    /// <summary>
    /// Raises an issue. <paramref name="submitNow"/> is what the wireframe's
    /// "Submit for Approval" button does; without it the issue is a draft only
    /// its author can see.
    /// </summary>
    public static SocialIssue Create(
        Guid tenantId,
        string title,
        string description,
        string category,
        string? locality,
        Guid submittedByMemberId,
        bool submitNow,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var issue = new SocialIssue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = description.Trim(),
            Category = category.Trim(),
            Locality = Normalize(locality),
            SubmittedByMemberId = submittedByMemberId,
            Status = submitNow ? IssueStatus.Submitted : IssueStatus.Draft,
            CreatedAt = now,
        };

        issue._history.Add(new IssueStatusHistory(
            issue.Id, null, issue.Status, submittedByMemberId, null, now));

        if (submitNow)
        {
            issue.Raise(new IssueSubmittedDomainEvent(
                issue.Id, tenantId, submittedByMemberId, issue.Category, now));
        }

        return issue;
    }

    /// <summary>
    /// Whether the Samaaj at large can see this: it is in front of the Panch,
    /// or the Panch has decided it. A closed issue that was withdrawn or
    /// rejected has no decision and stays private.
    /// </summary>
    public bool IsPublic =>
        Status == IssueStatus.DecisionPending
        || (Status == IssueStatus.Closed && DecidedAt is not null);

    /// <summary>Whether a reviewer still has something to decide.</summary>
    public bool AwaitsDecision => Status is IssueStatus.Submitted or IssueStatus.UnderReview;

    public bool IsAuthor(Guid memberId) => SubmittedByMemberId == memberId;

    /// <summary>Whether this move is legal at all, ignoring who is asking.</summary>
    public bool CanMoveTo(IssueStatus target) =>
        Transitions.Any(t => t.From == Status && t.To == target);

    /// <summary>Whether this move is one the author makes rather than a reviewer.</summary>
    public static bool IsAuthorMove(IssueStatus from, IssueStatus to) =>
        Transitions.Any(t => t.From == from && t.To == to && t.ByAuthor);

    /// <summary>
    /// Moves the issue, recording who did it and why. Returns false when the
    /// move is not in the transition table.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for having checked that the actor is entitled
    /// to make this kind of move - the aggregate knows the shape of the
    /// workflow, not who holds which permission.
    /// </remarks>
    public bool MoveTo(IssueStatus target, Guid actorUserId, string? reason, DateTimeOffset now)
    {
        if (!CanMoveTo(target))
        {
            return false;
        }

        var from = Status;
        Status = target;

        _history.Add(new IssueStatusHistory(
            Id, from, target, actorUserId, Normalize(reason), now));

        Raise(new IssueStatusChangedDomainEvent(
            Id, TenantId, SubmittedByMemberId, actorUserId,
            from.ToString(), target.ToString(), now));

        return true;
    }

    /// <summary>
    /// The Panch decides an issue that was put in front of it, and closes it.
    /// Returns false when the issue is not <see cref="IssueStatus.DecisionPending"/>.
    /// </summary>
    /// <remarks>
    /// Not a row in the transition table because it carries a payload: a
    /// decision is required, and the monitoring group is optional. The caller
    /// has checked that the actor sits on the Panch.
    ///
    /// The event carries ids only. The decision is a paragraph a Panch member
    /// wrote about a named person's concern, and audit-notification-service
    /// records payloads verbatim into an append-only table - the reason the
    /// submitted and status-changed events carry no text either.
    /// </remarks>
    public bool RecordDecision(
        string decision, Guid? monitoringGroupId, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decision);

        if (Status != IssueStatus.DecisionPending)
        {
            return false;
        }

        var from = Status;

        Status = IssueStatus.Closed;
        Decision = decision.Trim();
        MonitoringGroupId = monitoringGroupId;
        DecidedAt = now;
        DecidedBy = actorUserId;

        _history.Add(new IssueStatusHistory(
            Id, from, IssueStatus.Closed, actorUserId, null, now));

        Raise(new IssueStatusChangedDomainEvent(
            Id, TenantId, SubmittedByMemberId, actorUserId,
            from.ToString(), IssueStatus.Closed.ToString(), now));

        Raise(new IssueDecidedDomainEvent(
            Id, TenantId, SubmittedByMemberId, actorUserId, monitoringGroupId, now));

        return true;
    }

    /// <summary>
    /// Lets the author correct an issue that has not been decided yet, or one
    /// sent back to them. Returns false when it is too late to edit.
    /// </summary>
    /// <remarks>
    /// Editing stops once a reviewer has put it in front of the Panch: a
    /// reviewer who approved one thing and finds another being discussed has
    /// been made to endorse something they never read. An issue under review can still be
    /// corrected, because the wireframe's "Request Changes" only makes sense if
    /// changes are possible.
    /// </remarks>
    public bool Revise(
        string title, string description, string category, string? locality, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        if (Status is not (IssueStatus.Draft or IssueStatus.Submitted
            or IssueStatus.UnderReview or IssueStatus.ChangesRequested))
        {
            return false;
        }

        Title = title.Trim();
        Description = description.Trim();
        Category = category.Trim();
        Locality = Normalize(locality);

        return true;
    }

    /// <summary>
    /// Removes what an erased member wrote, keeping the row.
    /// </summary>
    /// <remarks>
    /// DPDP section 12, reaching this service through
    /// <c>identity.user.erased.v1</c>. Returns true when this call changed
    /// something, so the handler can count and stay idempotent — the event is
    /// delivered at least once.
    ///
    /// <b>The words go and the shape stays.</b> An issue is a container: a
    /// reviewer's decisions and the reasons they gave hang off it as history,
    /// and those are the reviewers' records, not the submitter's. Deleting the
    /// issue would take them with it — the same reason erasure leaves a
    /// household standing rather than deleting it out from under the people
    /// still in it. So the title, description and locality are replaced and the
    /// history is left alone.
    ///
    /// <b>Except the reasons this member wrote themselves.</b> A submitter is
    /// an actor in their own workflow — resubmitting after changes were asked
    /// for, for instance — and those reasons are their words like any other.
    ///
    /// The status is <b>not</b> moved. A published issue that vanishes from the
    /// list leaves a Samaaj wondering what happened to something it was told
    /// about, and the workflow record is the reviewers'. What it says is gone;
    /// that it existed is not the submitter's alone to erase.
    ///
    /// <c>SubmittedByMemberId</c> is deliberately not cleared, for the reason
    /// given in <c>TimelinePost.ErasePersonalDataOf</c> — it is counsel
    /// question 6, and should be answered once for every service holding a bare
    /// member id rather than differently here.
    /// </remarks>
    public bool ErasePersonalDataOf(Guid memberId)
    {
        var changed = false;

        if (SubmittedByMemberId == memberId && Description != ErasedPlaceholder)
        {
            Title = ErasedPlaceholder;
            Description = ErasedPlaceholder;
            Locality = null;

            changed = true;
        }

        foreach (var entry in _history.Where(h => h.ActorUserId == memberId))
        {
            changed |= entry.EraseReason();
        }

        // A Panch member's decision is their own words about the issue, like a
        // reviewer's reason. It goes; that the issue was decided, and when, does
        // not.
        if (DecidedBy == memberId && Decision is not null && Decision != ErasedPlaceholder)
        {
            Decision = ErasedPlaceholder;

            changed = true;
        }

        return changed;
    }

    /// <summary>What an issue reads as once its submitter has been erased.</summary>
    public const string ErasedPlaceholder = "[removed at the submitter's request]";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The workflow, in the order the member-portal's progress strip shows it.
/// </summary>
public enum IssueStatus
{
    /// <summary>Written but not sent. Only its author can see it.</summary>
    Draft = 1,

    /// <summary>Waiting for a reviewer.</summary>
    Submitted = 2,

    /// <summary>A reviewer has picked it up.</summary>
    UnderReview = 3,

    /// <summary>
    /// A reviewer has put it in front of the Samaaj Panch, which is discussing
    /// it. Visible to the Samaaj.
    /// </summary>
    DecisionPending = 4,

    /// <summary>Declined, with a reason the author is told.</summary>
    Rejected = 5,

    /// <summary>Sent back to the author to revise and resubmit.</summary>
    ChangesRequested = 6,

    /// <summary>
    /// Decided by the Panch, rejected and finished with, or withdrawn by its
    /// author.
    /// </summary>
    Closed = 8,
}
