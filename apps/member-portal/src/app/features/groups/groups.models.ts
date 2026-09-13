/**
 * Wire shapes for volunteer-groups-service, mirroring `GroupResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with,
 * checked against `VolunteerGroup.cs` rather than guessed - the rule this app
 * learned the hard way (see `apps/member-portal/CLAUDE.md`).
 */

/** `GroupStatus` in the domain. */
export type GroupStatus = 'Active' | 'Inactive';

/** `ApplicationStatus` in the domain. */
export type ApplicationStatus = 'Pending' | 'Accepted' | 'Rejected';

export interface VolunteerGroup {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly focusArea: string | null;

  /**
   * An id, not a name. The wireframe's card says "President: Rajesh Jain";
   * names live in member-family-service and resolving one would be a call per
   * card. The screen says whether the reader is the president instead, which
   * is the part that changes what they can do.
   */
  readonly presidentMemberId: string;
  readonly status: GroupStatus;
  readonly memberCount: number;

  /** Pending applications. Zero for anyone who cannot decide them. */
  readonly pendingApplicationCount: number;

  /** What this member's own application says, if they have one. */
  readonly myApplicationStatus: ApplicationStatus | null;
  readonly iAmAMember: boolean;
  readonly iAmThePresident: boolean;
  readonly createdAt: string;
}

export interface GroupMember {
  readonly memberId: string;
  readonly rolePosition: string | null;
  readonly joinedAt: string;
}

export interface GroupApplication {
  readonly id: string;
  readonly memberId: string;
  readonly note: string | null;
  readonly status: ApplicationStatus;
  readonly decidedBy: string | null;
  readonly decidedAt: string | null;
  readonly createdAt: string;
}

/**
 * A group with its members.
 *
 * Applications are deliberately not part of this: only the president may read
 * them, and a detail screen anybody can open should not be carrying a list of
 * who asked to join and was turned down.
 */
export interface GroupDetail {
  readonly group: VolunteerGroup;
  readonly members: readonly GroupMember[];
}

/**
 * How a group position is filled now: an election, not a click at accept-
 * time. `ElectionStatus`/`CandidateStatus` mirror `GroupElection.cs` and
 * `ElectionChildren.cs` in volunteer-groups-service - the same closed
 * enums Celebrities of Samaaj's own `voting.models.ts` checks against its
 * own domain rather than guessing.
 */
export type ElectionStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

export type CandidateStatus = 'Nominated' | 'Approved';

export interface Election {
  readonly id: string;
  readonly groupId: string;
  readonly positionTitle: string;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly status: ElectionStatus;

  /**
   * Whether nominations/voting are open *right now* - the status says so
   * *and* the clock agrees. Read these, never the status alone - the same
   * rule Celebrities of Samaaj's own screens already follow.
   */
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;

  /** The candidate this member voted for, if they have voted. */
  readonly myVoteCandidateId: string | null;
  readonly candidateCount: number;

  /** Set once the election is Published. Null until then. */
  readonly winnerMemberId: string | null;
  readonly publishedAt: string | null;
  readonly createdAt: string;
}

/**
 * One name on the ballot. Unlike Celebrities of Samaaj's own candidates, the
 * vote count is never hidden - a group election has no "results visibility"
 * setting to gate it on.
 */
export interface ElectionCandidate {
  readonly id: string;
  readonly memberId: string;
  readonly status: CandidateStatus;
  readonly nominatedBy: string;
  readonly votes: number;
}

export interface ElectionDetail {
  readonly election: Election;
  readonly candidates: readonly ElectionCandidate[];
}

/**
 * What casting a vote answers with. `accepted` is false when this member had
 * already voted - reported as success, because pressing the button twice is
 * not misconduct.
 */
export interface CastVoteResult {
  readonly electionId: string;
  readonly candidateId: string;
  readonly accepted: boolean;
}

/**
 * What nominating answers with. `nominated` is false when this member had
 * already been put forward; `candidateId` is then the candidacy that already
 * exists.
 */
export interface NominateResult {
  readonly electionId: string;
  readonly candidateId: string;
  readonly memberId: string;
  readonly nominated: boolean;
}

/**
 * What to call an election's current state on screen - reads the two window
 * flags first and the status only as a fallback, the identical shape
 * Celebrities of Samaaj's own `stageLabel` already uses and for the same
 * reason: an election left at NominationsOpen past its closing date is not
 * taking nominations.
 */
export function electionStageLabel(election: Election): string {
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
