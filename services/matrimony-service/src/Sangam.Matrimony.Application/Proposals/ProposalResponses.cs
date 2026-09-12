using Sangam.Matrimony.Application.Biodata;

namespace Sangam.Matrimony.Application.Proposals;

/// <summary>
/// A proposal as one of its two parties sees it. Carries the other party's
/// full biodata inline so the Proposals screen needs no follow-up call per
/// row - the same "resolve it once, in the response" shape member-family-service's
/// own directory search already uses.
/// </summary>
/// <remarks>
/// <see cref="OtherParty"/> is null when that member's biodata is gone
/// entirely - a fully erased account, not merely a withdrawn one, since a
/// withdrawn profile is still readable by its own owner and still resolves
/// here. A null OtherParty is the one case the screen has to say "this
/// member is no longer on the platform" rather than show a name.
/// </remarks>
public sealed record ProposalResponse(
    Guid Id,
    Guid FromMemberId,
    Guid ToMemberId,
    string Status,
    string? Message,
    DateTimeOffset SentAt,
    DateTimeOffset? RespondedAt,
    BiodataResponse? OtherParty);
