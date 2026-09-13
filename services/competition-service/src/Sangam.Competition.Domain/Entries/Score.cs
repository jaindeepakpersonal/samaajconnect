using Sangam.Competition.Domain.Common;

namespace Sangam.Competition.Domain.Entries;

/// <summary>
/// One judge's mark for one entry.
/// </summary>
/// <remarks>
/// Not part of any owning aggregate, and never loaded by one, for the same
/// reason <c>Vote</c> and <c>Bid</c> aren't: an entry in a large round can
/// carry several judges' marks, and loading them to record one more would
/// read the whole set on the path scoring happens on.
///
/// <b>Unlike a Vote, a Score is an upsert while its round is open.</b>
/// Correcting a misrecorded mark before judging closes is the ordinary
/// case here, not ballot-stuffing - there is no reason to copy Vote's
/// never-amend stance. The unique index on (EntryId, JudgeMemberId) is what
/// makes "upsert" well-defined rather than "insert, sometimes twice"; see
/// <c>IScoreRepository.UpsertAsync</c>.
/// </remarks>
public sealed class Score : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EntryId { get; private set; }
    public Guid JudgeMemberId { get; private set; }
    public int Value { get; private set; }
    public string? Comments { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Score() { }   // EF Core

    public Score(
        Guid tenantId, Guid entryId, Guid judgeMemberId, int value, string? comments, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        EntryId = entryId;
        JudgeMemberId = judgeMemberId;
        Value = value;
        Comments = Trim(comments);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Update(int value, string? comments, DateTimeOffset now)
    {
        Value = value;
        Comments = Trim(comments);
        UpdatedAt = now;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
