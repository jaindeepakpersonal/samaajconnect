using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Abstractions;

public interface IRoundResultRepository
{
    Task<RoundResult?> FindByRoundAsync(Guid roundId, CancellationToken cancellationToken = default);

    void Add(RoundResult result);
}
