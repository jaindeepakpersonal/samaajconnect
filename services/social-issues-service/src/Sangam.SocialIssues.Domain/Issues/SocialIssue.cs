using Sangam.SocialIssues.Domain.Common;

namespace Sangam.SocialIssues.Domain.Issues;

/// <summary>
/// Something a member wants the Samaaj to do something about: road safety near
/// the school, support for elderly members, lighting in the community park.
/// </summary>
/// <remarks>
/// This is the platform's longest-lived workflow — eight states — and the
/// member-portal wireframe shows it as a progress strip: Submitted → Under
/// Review → Approved → Published. The transitions are declared as a table in
/// <see cref="Transitions"/> rather than scattered through methods, because
/// eight states have fifty-odd plausible transitions and the only way to see
/// which are allowed is to have them written down in one place.
///
/// <b>Every transition is recorded.</b> <see cref="IssueStatusHistory"/> is
/// append-only within the aggregate: nothing changes or removes a row. A member
/// whose issue was rejected will ask why, and a Samaaj that cannot answer has
/// failed them twice.
///
/// The subtitle on the member's screen — "member submissions are published only
/// after valid approval" — is the invariant this aggregate exists to hold.
/// Publishing is reachable only from <see cref="IssueStatus.Approved"/>, and
/// approval is reachable only from a review.
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
        (IssueStatus.UnderReview, IssueStatus.Approved, false),
        (IssueStatus.UnderReview, IssueStatus.Rejected, false),
        (IssueStatus.UnderReview, IssueStatus.ChangesRequested, false),

        // Deciding straight from Submitted, without picking it up first. The
        // wireframe's queue offers Approve, Reject and Request Changes on a
        // submitted issue, so requiring a separate "start review" click would
        // be a step the design does not have.
        (IssueStatus.Submitted, IssueStatus.Approved, false),
        (IssueStatus.Submitted, IssueStatus.Rejected, false),
        (IssueStatus.Submitted, IssueStatus.ChangesRequested, false),

        // Publishing is reachable only from Approved. This single row is the
        // "published only after valid approval" promise.
        (IssueStatus.Approved, IssueStatus.Published, false),

        // A published issue is closed when it has been dealt with; a rejected
        // one is already over.
        (IssueStatus.Published, IssueStatus.Closed, false),
        (IssueStatus.Rejected, IssueStatus.Closed, false),
        (IssueStatus.Approved, IssueStatus.Closed, false),
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
    public DateTimeOffset? PublishedAt { get; private set; }

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

    /// <summary>Whether the Samaaj at large can see this.</summary>
    public bool IsPublic => Status is IssueStatus.Published or IssueStatus.Closed;

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

        if (target == IssueStatus.Published)
        {
            PublishedAt = now;
        }

        _history.Add(new IssueStatusHistory(
            Id, from, target, actorUserId, Normalize(reason), now));

        Raise(new IssueStatusChangedDomainEvent(
            Id, TenantId, SubmittedByMemberId, actorUserId,
            from.ToString(), target.ToString(), now));

        if (target == IssueStatus.Published)
        {
            Raise(new IssuePublishedDomainEvent(Id, TenantId, Category, Locality, now));
        }

        return true;
    }

    /// <summary>
    /// Lets the author correct an issue that has not been decided yet, or one
    /// sent back to them. Returns false when it is too late to edit.
    /// </summary>
    /// <remarks>
    /// Editing stops once a reviewer has approved or published it: a reviewer
    /// who approved one thing and finds another published has been made to
    /// endorse something they never read. An issue under review can still be
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

    /// <summary>Accepted, and awaiting publication.</summary>
    Approved = 4,

    /// <summary>Declined, with a reason the author is told.</summary>
    Rejected = 5,

    /// <summary>Sent back to the author to revise and resubmit.</summary>
    ChangesRequested = 6,

    /// <summary>Visible to the Samaaj.</summary>
    Published = 7,

    /// <summary>Dealt with, withdrawn, or finished.</summary>
    Closed = 8,
}
