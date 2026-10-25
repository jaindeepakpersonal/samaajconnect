using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Abstractions;

public interface ICompetitionResultRepository
{
    Task<CompetitionResult?> FindByCompetitionAsync(Guid competitionId, CancellationToken cancellationToken = default);

    void Add(CompetitionResult result);
}
