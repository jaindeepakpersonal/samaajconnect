using Sangam.Timeline.Domain.Common;

namespace Sangam.Timeline.Domain.Posts;

/// <summary>
/// One item on a Samaaj's timeline: an announcement from the Samaaj, or a post
/// by a member.
/// </summary>
/// <remarks>
/// The moderation lifecycle is the substance of this aggregate, and the two
/// post types travel through it differently on purpose.
///
/// A <see cref="PostType.MemberPost"/> is created
/// <see cref="PostStatus.PendingReview"/> and is invisible to the Samaaj until a
/// moderator approves it. The member-portal wireframe's button says "Post for
/// Review", not "Post", and that is the honest description: this is a community
/// organisation's shared space, not a broadcast channel.
///
/// An <see cref="PostType.Announcement"/> is created
/// <see cref="PostStatus.Approved"/>. It can only be created by someone holding
/// Timeline.Moderate in the first place, so routing it through a queue would
/// mean an administrator approving their own post - a step that reads as a
/// control and is not one.
/// </remarks>
public sealed class TimelinePost : AggregateRoot, ITenantScopedEntity
{
    private readonly List<PostComment> _comments = [];
    private readonly List<PostReaction> _reactions = [];
    private readonly List<ModerationAction> _moderationActions = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AuthorMemberId { get; private set; }
    public PostType Type { get; private set; }

    /// <summary>Headline. The wireframe shows one on every post.</summary>
    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;
    public PostStatus Status { get; private set; }

    /// <summary>
    /// How many members have reported this post. A moderator sees the count,
    /// never who reported - a community organisation is a place where people
    /// know each other, and a visible reporter list would stop anyone reporting
    /// anything.
    /// </summary>
    public int ReportCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ModeratedAt { get; private set; }

    public IReadOnlyCollection<PostComment> Comments => _comments.AsReadOnly();
    public IReadOnlyCollection<PostReaction> Reactions => _reactions.AsReadOnly();
    public IReadOnlyCollection<ModerationAction> ModerationActions => _moderationActions.AsReadOnly();

    private TimelinePost() { }   // EF Core

    public static TimelinePost Create(
        Guid tenantId,
        Guid authorMemberId,
        PostType type,
        string title,
        string body,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var post = new TimelinePost
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AuthorMemberId = authorMemberId,
            Type = type,
            Title = title.Trim(),
            Body = body.Trim(),
            Status = type == PostType.Announcement
                ? PostStatus.Approved
                : PostStatus.PendingReview,
            CreatedAt = createdAt,
        };

        post.Raise(new PostSubmittedDomainEvent(
            post.Id,
            tenantId,
            authorMemberId,
            type.ToString(),
            post.Status.ToString(),
            createdAt));

        return post;
    }

    /// <summary>Whether this post is visible to the Samaaj at large.</summary>
    public bool IsPubliclyVisible => Status == PostStatus.Approved;

    /// <summary>
    /// The decisions worth offering a moderator looking at this post.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here rather than in the screen, for the reason the social-issues
    /// transition table is: a moderation queue that works out its own buttons
    /// keeps a second copy of this rule, and the first time a state is added
    /// the screen is confidently wrong. The queue carries this list and the
    /// screen renders exactly what it is given.
    /// </para>
    /// <para>
    /// It is what is <i>sensible to offer</i>, not a gate.
    /// <see cref="Moderate"/> stays permissive and reports a decision that
    /// changes nothing as success, because two moderators reaching the same
    /// conclusion is agreement rather than an error. Narrowing it here is about
    /// not putting a button in front of somebody that would do nothing, or that
    /// means the same as the one beside it: Approve and Restore both end at
    /// Approved, so a post is only ever offered whichever of the two describes
    /// what is actually happening to it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ModerationDecision> AvailableDecisions => Status switch
    {
        // Waiting to be seen for the first time: publish it or refuse it.
        PostStatus.PendingReview => [ModerationDecision.Approve, ModerationDecision.Reject],

        // Already published, and in the queue because somebody reported it.
        // Taking it down is Hide, not Reject - Reject is for something that was
        // never published, and the member has already seen this one go up.
        PostStatus.Approved => [ModerationDecision.Hide],

        // A moderator reconsidering their own refusal.
        PostStatus.Rejected => [ModerationDecision.Approve],

        // Restore rather than Approve, so the moderation history reads as what
        // happened: this post went up, came down, and went back up.
        PostStatus.Hidden => [ModerationDecision.Restore],

        // A draft is its author's, and is not in anybody's queue.
        _ => [],
    };

    /// <summary>
    /// Records a moderator's decision. Returns false when the post is already in
    /// that state, so a second click is not a second audit entry.
    /// </summary>
    public bool Moderate(
        ModerationDecision decision,
        Guid actorUserId,
        string? reason,
        DateTimeOffset now)
    {
        var next = decision switch
        {
            ModerationDecision.Approve => PostStatus.Approved,
            ModerationDecision.Reject => PostStatus.Rejected,
            ModerationDecision.Hide => PostStatus.Hidden,
            ModerationDecision.Restore => PostStatus.Approved,
            _ => Status,
        };

        if (Status == next)
        {
            return false;
        }

        var previous = Status;
        Status = next;
        ModeratedAt = now;

        _moderationActions.Add(new ModerationAction(
            Id, actorUserId, decision, Normalize(reason), now));

        // Restoring a hidden post clears the reports that led to hiding it.
        // Leaving them would mean the next moderator sees a post that looks
        // freshly complained about when the complaints were already answered.
        if (decision == ModerationDecision.Restore)
        {
            ReportCount = 0;
        }

        Raise(new PostModeratedDomainEvent(
            Id,
            TenantId,
            AuthorMemberId,
            actorUserId,
            decision.ToString(),
            previous.ToString(),
            Status.ToString(),
            now));

        return true;
    }

    /// <summary>
    /// Adds a comment. Only an approved post can be commented on - a post still
    /// in the queue is not visible, so a comment on one could only come from
    /// somebody who guessed its id.
    /// </summary>
    public PostComment? Comment(Guid authorMemberId, string body, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        if (!IsPubliclyVisible)
        {
            return null;
        }

        var comment = new PostComment(Id, authorMemberId, body.Trim(), now);

        _comments.Add(comment);

        return comment;
    }

    /// <summary>
    /// Sets, changes or clears this member's reaction. Returns the reaction
    /// now held, or null when they have removed it.
    /// </summary>
    /// <remarks>
    /// One reaction per member, replaced rather than added to. Reacting twice
    /// with the same type removes it, which is how every product that has this
    /// button behaves and what a member will expect without being told.
    /// </remarks>
    public ReactionType? React(Guid memberId, ReactionType reaction, DateTimeOffset now)
    {
        var existing = _reactions.FirstOrDefault(r => r.MemberId == memberId);

        if (existing is not null)
        {
            _reactions.Remove(existing);

            if (existing.Type == reaction)
            {
                return null;
            }
        }

        _reactions.Add(new PostReaction(Id, memberId, reaction, now));

        return reaction;
    }

    /// <summary>
    /// A member reports this post. Returns true when the report moved it into
    /// the moderation queue.
    /// </summary>
    /// <remarks>
    /// Reporting does not hide a post by itself. A single member being able to
    /// remove anything from a community's timeline would be a heckler's veto,
    /// so the report raises the count and puts the post in front of a human;
    /// the decision stays with the moderator.
    /// </remarks>
    public bool Report(Guid reporterMemberId, DateTimeOffset now)
    {
        if (reporterMemberId == AuthorMemberId || !IsPubliclyVisible)
        {
            return false;
        }

        ReportCount++;

        Raise(new PostReportedDomainEvent(Id, TenantId, ReportCount, now));

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
    /// <b>The words go and the shape stays.</b> A post is a container: other
    /// members' comments and reactions hang off it, and deleting it would take
    /// their records with it — the same reason erasure leaves a household
    /// standing rather than deleting it out from under the people still in it.
    /// So the title and body are replaced and the post is hidden, which stops
    /// it being displayed to anyone.
    ///
    /// <b>Comments this member left on other people's posts go too</b>, because
    /// they are equally their words and are not containers of anything.
    /// Reactions carry no text and are left alone.
    ///
    /// <c>AuthorMemberId</c> is deliberately <b>not</b> cleared. Once
    /// identity-tenant-service and member-family-service have erased, that id
    /// resolves to nobody anywhere on the platform, which is the same position
    /// the other services holding bare member ids are in; whether that is
    /// sufficient is open counsel question 6 in DPDP-COMPLIANCE.md, and it
    /// should be answered once for all of them rather than differently here.
    /// </remarks>
    public bool ErasePersonalDataOf(Guid memberId)
    {
        var changed = false;

        if (AuthorMemberId == memberId && !IsErased)
        {
            Title = ErasedPlaceholder;
            Body = ErasedPlaceholder;
            Status = PostStatus.Hidden;

            changed = true;
        }

        foreach (var comment in _comments.Where(c => c.AuthorMemberId == memberId))
        {
            changed |= comment.EraseBody(ErasedPlaceholder);
        }

        return changed;
    }

    /// <summary>What a post reads as once its author has been erased.</summary>
    public const string ErasedPlaceholder = "[removed at the author's request]";

    private bool IsErased => Body == ErasedPlaceholder && Status == PostStatus.Hidden;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum PostType
{
    /// <summary>From the Samaaj. Published without review; see the remarks on the aggregate.</summary>
    Announcement = 1,

    /// <summary>From a member. Reviewed before anyone else sees it.</summary>
    MemberPost = 2,
}

public enum PostStatus
{
    Draft = 1,
    PendingReview = 2,
    Approved = 3,
    Rejected = 4,
    Hidden = 5,
}

public enum ModerationDecision
{
    Approve = 1,
    Reject = 2,
    Hide = 3,
    Restore = 4,
}

public enum ReactionType
{
    Appreciate = 1,
    Support = 2,
    Celebrate = 3,
}
