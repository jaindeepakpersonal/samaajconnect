using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Abstractions;

public interface ICampaignRepository
{
    /// <summary>
    /// Tenant-filtered, with candidates loaded - and <b>never</b> votes.
    /// </summary>
    /// <remarks>
    /// Candidates are bounded and few; votes are neither. Loading votes here
    /// would put a full table read on the vote path, which is the one place on
    /// this platform that cannot afford it.
    /// </remarks>
    Task<VotingCampaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VotingCampaign>> ListAsync(CancellationToken cancellationToken = default);

    void Add(VotingCampaign campaign);
}

/// <summary>
/// Votes, kept away from the campaign aggregate on purpose.
/// </summary>
/// <remarks>
/// Every method here is a targeted query or a single insert. Nothing loads a
/// campaign's votes into memory, because at the close of voting that is exactly
/// what would fall over.
/// </remarks>
public interface IVoteRepository
{
    /// <summary>Has this member already voted in this campaign?</summary>
    /// <remarks>
    /// A courtesy check for a decent error message, and <b>not</b> the
    /// guarantee: two requests can both pass it. The unique index on
    /// (CampaignId, VoterMemberId) is what actually prevents a double vote.
    /// </remarks>
    Task<Vote?> FindForVoterAsync(
        Guid campaignId, Guid voterMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a vote directly, returning false when the unique index refuses
    /// it because this member has already voted.
    /// </summary>
    /// <remarks>
    /// Its own connection and its own transaction, one statement. This is the
    /// contended path, and holding a request-scoped transaction open across it
    /// would serialise voters against each other for no benefit.
    /// </remarks>
    Task<bool> TryCastAsync(Vote vote, CancellationToken cancellationToken = default);

    /// <summary>The tally, as a GROUP BY rather than a count over loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid campaignId, CancellationToken cancellationToken = default);

    Task<CampaignResult?> FindResultAsync(
        Guid campaignId, CancellationToken cancellationToken = default);

    void AddResult(CampaignResult result);
}
