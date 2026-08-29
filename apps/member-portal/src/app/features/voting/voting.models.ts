/**
 * Wire shapes for celebrity-voting-service, mirroring `CampaignResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with,
 * checked against `VotingCampaign.cs` and `Candidate.cs` rather than guessed.
 */

/** `CampaignStatus` in the domain, in the order it moves through. */
export type CampaignStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

/** `CandidateStatus` in the domain. */
export type CandidateStatus = 'Nominated' | 'Approved';

/** `ResultsVisibility` in the domain. */
export type ResultsVisibility = 'Live' | 'HiddenUntilClose';

export interface Campaign {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly topN: number;
  readonly resultsVisibility: ResultsVisibility;
  readonly status: CampaignStatus;

  /**
   * Whether nominations are open **right now** - the status says so *and* the
   * clock agrees.
   *
   * The screen uses these two rather than deriving from `status`, because a
   * campaign left at NominationsOpen past its closing date is not taking
   * nominations, and only the server knows the time it is deciding against.
   */
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;

  /** The candidate this member voted for, if they have voted. */
  readonly myVoteCandidateId: string | null;
  readonly candidateCount: number;
  readonly createdAt: string;
}

/**
 * One name on the ballot.
 *
 * `votes` is null when the tally is not visible to this caller - a campaign set
 * to HiddenUntilClose, still running, seen by an ordinary member. **Null rather
 * than zero, and the screen must keep the difference**: zero is a claim, and
 * showing it would tell a member nobody had voted for someone when the truth is
 * that they are not being shown the count.
 */
export interface Candidate {
  readonly id: string;
  readonly memberId: string;
  readonly category: string | null;
  readonly status: CandidateStatus;
  readonly nominatedBy: string;
  readonly votes: number | null;
}

export interface CampaignDetail {
  readonly campaign: Campaign;
  readonly candidates: readonly Candidate[];

  /**
   * False when this caller is being shown a ballot without counts.
   *
   * The one flag that separates "no votes yet" from "you may not see the
   * votes". Without it a screen has to guess from a null, and would guess
   * wrong on an empty campaign.
   */
  readonly tallyVisible: boolean;
}

/**
 * What casting a vote answers with.
 *
 * `accepted` is false when this member had already voted - reported as success,
 * because pressing the button twice is not an error and the response says what
 * they hold either way. The unique index on (campaign, voter), not the portal,
 * is what actually prevents the second vote.
 */
export interface VoteResult {
  readonly campaignId: string;
  readonly candidateId: string;
  readonly accepted: boolean;
}

/**
 * What nominating answers with.
 *
 * `nominated` is false when this member had already been put forward;
 * `candidateId` is then the candidacy that already exists. One candidacy per
 * member per campaign is what stops a vote splitting.
 */
export interface NominateResult {
  readonly campaignId: string;
  readonly candidateId: string;
  readonly memberId: string;
  readonly nominated: boolean;
}

export interface ResultEntry {
  readonly rank: number;
  readonly candidateId: string;
  readonly memberId: string;
  readonly votes: number;
}

/**
 * The published ranking, as frozen when it was announced.
 *
 * Read from the stored result rather than recomputed, so it cannot move after
 * the Samaaj has been told - which is what the wireframe means by "Locked after
 * publication".
 */
export interface CampaignResult {
  readonly campaignId: string;
  readonly ranking: readonly ResultEntry[];
  readonly publishedBy: string;
  readonly publishedAt: string;
}

/**
 * What to call a campaign's current state on screen.
 *
 * Reads `acceptsNominations`/`acceptsVotes` first and the status only as a
 * fallback, for the same reason every other line on these screens does: a
 * campaign left at `NominationsOpen` past its closing date is not taking
 * nominations. Labelling it from the status alone put a pill reading
 * "Nominations open" directly above a line reading "Nominations have closed",
 * which is worse than either sentence on its own - a reader has to work out
 * which half of the card to believe.
 *
 * So the two states that carry a deadline in their name get a label for the
 * closed half of their life, and the ones that do not are named as they are.
 */
export function stageLabel(campaign: Campaign): string {
  if (campaign.acceptsNominations) {
    return 'Nominations open';
  }

  if (campaign.acceptsVotes) {
    return 'Voting open';
  }

  switch (campaign.status) {
    case 'Draft':
      return 'Not started';
    case 'NominationsOpen':
      return 'Nominations closed';
    case 'VotingOpen':
    case 'Closed':
      return 'Voting closed';
    case 'Published':
      return 'Result published';
  }
}
