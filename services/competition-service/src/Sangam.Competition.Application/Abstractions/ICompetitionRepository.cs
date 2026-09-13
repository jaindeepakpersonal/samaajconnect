using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Application.Abstractions;

/// <summary>
/// Tenant-filtered, with registrations, judges and rounds loaded - all
/// bounded and few, the same reasoning <c>ICampaignRepository</c> gives for
/// loading a campaign's candidates. Never entries or scores; see
/// <see cref="IEntryRepository"/>/<see cref="IScoreRepository"/>.
/// </summary>
public interface ICompetitionRepository
{
    Task<CompetitionAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompetitionAggregate>> ListAsync(CancellationToken cancellationToken = default);

    void Add(CompetitionAggregate competition);
}
