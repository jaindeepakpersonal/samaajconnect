namespace Sangam.SocialIssues.Domain.Issues;

/// <summary>
/// One step in an issue's life: what it moved from, what to, who moved it and
/// why.
/// </summary>
/// <remarks>
/// Append-only within the aggregate - there is no method that changes or
/// removes one. A member whose issue was rejected will ask why, and a Samaaj
/// that cannot answer has failed them twice: once by declining and once by
/// being unable to say so.
///
/// <see cref="FromStatus"/> is null on the first row, which records the state
/// the issue was created in.
/// </remarks>
public sealed class IssueStatusHistory
{
    public Guid Id { get; private set; }
    public Guid IssueId { get; private set; }

    /// <summary>Null on the row that records creation.</summary>
    public IssueStatus? FromStatus { get; private set; }

    public IssueStatus ToStatus { get; private set; }
    public Guid ActorUserId { get; private set; }

    /// <summary>Why. Required for the decisions that need explaining.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private IssueStatusHistory() { }   // EF Core

    internal IssueStatusHistory(
        Guid issueId,
        IssueStatus? fromStatus,
        IssueStatus toStatus,
        Guid actorUserId,
        string? reason,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        IssueId = issueId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorUserId = actorUserId;
        Reason = reason;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Drops the reason this actor wrote. Returns false when there was none, so
    /// a redelivered erasure changes nothing.
    /// </summary>
    /// <remarks>
    /// Nulled rather than replaced with a placeholder: a reason is already
    /// optional here, so null is a shape every reader of this row already
    /// handles. The transition itself — from what, to what, by whom, when — is
    /// the workflow record and stays.
    ///
    /// Internal: only <see cref="SocialIssue.ErasePersonalDataOf"/> calls it.
    /// </remarks>
    internal bool EraseReason()
    {
        if (Reason is null)
        {
            return false;
        }

        Reason = null;

        return true;
    }
}
