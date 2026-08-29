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
