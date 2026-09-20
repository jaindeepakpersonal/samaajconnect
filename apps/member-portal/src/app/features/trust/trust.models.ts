/**
 * Wire shapes for trust-service, mirroring its own Response records.
 *
 * The string unions are the names the service serialises its enums with.
 */

export type TrustStatus = 'Active' | 'Inactive';

export interface Trust {
  readonly id: string;
  readonly localityId: string;
  readonly name: string;
  readonly status: TrustStatus;
  readonly createdAt: string;
}

export type TrustMemberStatus = 'Active' | 'Removed';

/** One row on a Trust's roster. */
export interface TrustMember {
  readonly id: string;
  readonly trustId: string;
  readonly memberId: string;
  readonly positionTitle: string | null;
  readonly status: TrustMemberStatus;
  readonly addedBy: string;
  readonly addedAt: string;
  readonly removedBy: string | null;
  readonly removedAt: string | null;
}

/** `TrustElectionStatus` in the domain, in the order it moves through. */
export type TrustElectionStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

export interface TrustElection {
  readonly id: string;
  readonly trustId: string;
  readonly positionTitle: string;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly status: TrustElectionStatus;

  /** Right now: the status says so *and* the clock agrees. */
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;

  /** The candidate this member voted for, or null if they have not voted. */
  readonly myVoteCandidateId: string | null;
  readonly candidateCount: number;

  /** Set once the election is Published. Null until then. */
  readonly winnerMemberId: string | null;
  readonly publishedAt: string | null;
  readonly createdAt: string;
}

/** A Trust election's vote counts are never hidden - it has no results-visibility setting. */
export interface TrustElectionCandidate {
  readonly id: string;
  readonly memberId: string;
  readonly status: 'Nominated' | 'Approved';
  readonly nominatedBy: string;
  readonly votes: number;
}

export interface TrustElectionDetail {
  readonly election: TrustElection;
  readonly candidates: readonly TrustElectionCandidate[];
}

export interface NominateTrustCandidateResult {
  readonly electionId: string;
  readonly candidateId: string;
  readonly memberId: string;

  /** False when they had already been put forward - a repeat is not an error. */
  readonly nominated: boolean;
}

export interface CastTrustVoteResult {
  readonly electionId: string;
  readonly candidateId: string;

  /** False when this member had already voted - the unique index is what prevents it, not this call. */
  readonly accepted: boolean;
}

/** What the election list says about where an election stands, from the flags then the status. */
export function trustElectionStageLabel(election: TrustElection): string {
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
