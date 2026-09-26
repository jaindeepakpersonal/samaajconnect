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
  readonly presidentMemberId: string | null;
  readonly status: GroupStatus;
  readonly memberCount: number;

  /** Pending applications. Zero for anyone who cannot decide them. */
  readonly pendingApplicationCount: number;

  /** What this member's own application says, if they have one. */
  readonly myApplicationStatus: ApplicationStatus | null;
  readonly iAmAMember: boolean;
  readonly iAmThePresident: boolean;

  /**
   * May decide join requests and run elections: the president, or - while the
   * group has none - a Samaaj admin. Read this rather than re-deriving it.
   */
  readonly iCanLead: boolean;
  readonly createdAt: string;

  /**
   * May define the group's roles, assign them and run its elections: the
   * president, or an administrator of the group at any time.
   */
  readonly iCanRunElections: boolean;
}

export interface GroupMember {
  readonly memberId: string;

  /** The title of the role they hold. Every member holds one. */
  readonly rolePosition: string | null;
  readonly joinedAt: string;
  readonly roleId: string;
}

/**
 * One of the group's volunteer roles. Volunteer and President are built in and
 * not `isEditable`; `isElected` is every role but Volunteer. `seats` is null for
 * Volunteer: no limit.
 */
export interface GroupRole {
  readonly id: string;
  readonly title: string;
  readonly kind: 'Volunteer' | 'President' | 'Elected';
  readonly seats: number | null;
  readonly holderCount: number;
  readonly isEditable: boolean;
  readonly isElected: boolean;
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
  readonly roles: readonly GroupRole[];
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

  /** The candidates this member picked, if they have voted. Empty until then. */
  readonly myVoteCandidateIds: readonly string[];
  readonly candidateCount: number;

  /** Set once the election is Published, in ranked order. Empty until then. */
  readonly winnerMemberIds: readonly string[];
  readonly publishedAt: string | null;
  readonly createdAt: string;

  /** The group role it fills; `positionTitle` is that role's title when it was opened. */
  readonly roleId: string;

  /** How many winners it produces, and how many candidates each voter may pick. */
  readonly seats: number;
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
  readonly candidateIds: readonly string[];
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
 *
 * The fallback also looks at the clock. A status of VotingOpen whose voting
 * window has not started yet is not "Voting closed" - it is about to open, and
 * saying closed sent people looking for a fault when the only thing wrong was
 * the date the organiser picked.
 */
export function electionStageLabel(election: Election, now: number = Date.now()): string {
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
      return now < Date.parse(election.nominationStartAt) ? 'Nominations open soon' : 'Nominations closed';
    case 'VotingOpen':
      return now < Date.parse(election.votingStartAt) ? 'Voting opens soon' : 'Voting closed';
    case 'Closed':
      return 'Voting closed';
    case 'Published':
      return 'Result published';
  }
}

/**
 * Why a member cannot vote right now, in a sentence - or null when nothing needs
 * saying (they can, they already have, or the election is over).
 *
 * The vote button is drawn only when every one of several things is true: the
 * reader belongs to the group, the status is VotingOpen, the clock is inside the
 * voting window, somebody is on the ballot, and they have not voted. When any one
 * fails the button is simply absent, and a missing button with no reason reads
 * as a bug. This names the one that is missing, in the order a person would fix
 * them.
 */
export function votingNote(
  election: Election,
  reader: { readonly inGroup: boolean; readonly canLead: boolean },
  onBallot: number,
  now: number = Date.now(),
): string | null {
  if (election.status === 'Published' || election.myVoteCandidateIds.length > 0) {
    return null;
  }

  if (!reader.inGroup) {
    return 'Only members of this group can vote. Ask to join it to take part.';
  }

  const opens = new Date(election.votingStartAt).toLocaleString(undefined, {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  });

  if (election.status === 'Draft' || election.status === 'NominationsOpen') {
    return (
      'Voting has not been opened yet. ' +
      (reader.canLead
        ? 'Once the ballot is set, use "Open voting" above.'
        : 'The organiser opens it once nominations are done.') +
      ` Its window starts ${opens}.`
    );
  }

  if (election.status === 'Closed' || now > Date.parse(election.votingEndAt)) {
    return 'Voting is closed.';
  }

  if (!election.acceptsVotes) {
    return `Voting has been switched on, but its window does not start until ${opens}. The vote button appears then.`;
  }

  return onBallot === 0
    ? 'Voting is open, but nobody is on the ballot yet. Nominations have to be approved first.'
    : null;
}
