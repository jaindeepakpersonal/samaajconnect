using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Abstractions;

/// <summary>
/// Scores, kept away from <c>Entry</c> on purpose - see <c>Score</c>'s own
/// remarks. Nothing here loads a whole round's scores into memory; the
/// average and "my own marks" methods are both database-side aggregates.
/// </summary>
public interface IScoreRepository
{
    Task<Score?> FindAsync(
        Guid entryId, Guid judgeMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a judge's mark, on its own scope - inserting it the first
    /// time, updating it on every call after. See the implementation for why
    /// this runs off the request's own connection.
    /// </summary>
    Task<Score> UpsertAsync(
        Guid tenantId,
        Guid entryId,
        Guid judgeMemberId,
        int value,
        string? comments,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Each entry's average mark across judges, as a GROUP BY rather than loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, double>> AverageByEntryAsync(
        IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken = default);

    /// <summary>This one judge's own marks across the given entries.</summary>
    Task<IReadOnlyDictionary<Guid, int>> ListMineAsync(
        IReadOnlyList<Guid> entryIds, Guid judgeMemberId, CancellationToken cancellationToken = default);
}
