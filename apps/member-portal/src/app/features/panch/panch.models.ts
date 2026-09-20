/**
 * Wire shapes for panch-service, mirroring `ElectionResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with.
 */

/** `ElectionStatus` in the domain, in the order it moves through. */
export type ElectionStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

/** `CandidateStatus` in the domain. */
export type CandidateStatus = 'Nominated' | 'Approved';

/** `ResultsVisibility` in the domain. */
export type ResultsVisibility = 'Live' | 'HiddenUntilClose';

export interface Election {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;

  /** The Panch is this many members, and a voter may back up to this many. */
  readonly seats: number;
  readonly resultsVisibility: ResultsVisibility;
  readonly status: ElectionStatus;

  /**
   * Whether nominations are open **right now** - the status says so *and* the
   * clock agrees. Only the server knows the time it is deciding against, so
   * the screen reads these rather than deriving from the status.
   */
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;

  /** The candidates this member backed, or null if they have not voted. */
  readonly myChoices: readonly string[] | null;
  readonly candidateCount: number;
  readonly createdAt: string;
}

export interface Candidate {
  readonly id: string;
  readonly memberId: string;
  readonly status: CandidateStatus;
  readonly nominatedBy: string;

  /** Null when the tally is not visible to this caller. Null is not zero. */
  readonly votes: number | null;
}

export interface ElectionDetail {
  readonly election: Election;
  readonly candidates: readonly Candidate[];

  /** False when this caller is shown a ballot without counts. */
  readonly tallyVisible: boolean;
}

export interface ResultEntry {
  readonly rank: number;
  readonly candidateId: string;
  readonly memberId: string;
  readonly votes: number;
}

export interface ElectionResult {
  readonly electionId: string;
  readonly ranking: readonly ResultEntry[];
  readonly seatedMemberIds: readonly string[];
  readonly publishedBy: string;
  readonly publishedAt: string;
}

export interface NominateResult {
  readonly electionId: string;
  readonly candidateId: string;
  readonly memberId: string;

  /** False when they had already been put forward - a repeat is not an error. */
  readonly nominated: boolean;
}

export interface BallotResult {
  readonly electionId: string;
  readonly choices: readonly string[];

  /** False when this member had already voted; `choices` is then the ballot they hold. */
  readonly accepted: boolean;
}

export interface PanelMember {
  readonly memberId: string;
  readonly electionId: string;
  readonly seatedAt: string;
}

export interface Panel {
  readonly members: readonly PanelMember[];
  readonly seats: number;

  /** Whether the asker sits on the Panch - the screen never works it out from the list. */
  readonly viewerIsMember: boolean;
}

/** What the election list says about where an election stands, from the flags then the status. */
export function stageLabel(election: Election): string {
  if (election.acceptsNominations) {
    return 'Nominations open';
  }

  if (election.acceptsVotes) {
    return 'Voting open';
  }

  switch (election.status) {
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
