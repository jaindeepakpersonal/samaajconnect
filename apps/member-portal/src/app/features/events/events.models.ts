/**
 * Wire shapes for events-service, mirroring `EventResponses.cs`.
 *
 * The string unions below are the names the service actually serialises its
 * enums with, checked against `SamaajEvent.cs` rather than guessed. That check
 * is not optional: these are strings compared against strings on the wire, so a
 * wrong name is not a type error - the comparison silently never matches and
 * the screen renders a state that is not the one the member is in. Timeline
 * shipped with two of those (see `apps/member-portal/CLAUDE.md`).
 */

/** `OrganizerType` in the domain. */
export type OrganizerType = 'Samaaj' | 'VolunteerGroup';

/** `EventStatus` in the domain. */
export type EventStatus = 'Draft' | 'Published' | 'Cancelled';

/** `RegistrationStatus` in the domain. */
export type RegistrationStatus = 'Registered' | 'Waitlisted' | 'Cancelled';

export interface SamaajEvent {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly startAt: string;
  readonly endAt: string | null;
  readonly venue: string | null;
  readonly organizerType: OrganizerType;

  /**
   * An id, not a name. Group names live in volunteer-groups-service, and
   * resolving one would be a call per row for a list. The screen names the
   * kind of organiser instead of inventing a name it cannot get.
   */
  readonly organizerId: string | null;
  readonly status: EventStatus;
  readonly registrationEnabled: boolean;

  /** Null means no limit, which is a different thing from a limit of zero. */
  readonly capacity: number | null;
  readonly registeredCount: number;
  readonly waitlistedCount: number;

  /** True when there is a capacity and it is reached. */
  readonly isFull: boolean;

  /**
   * What this member's own registration says, or null if they never
   * registered. This is what the wireframe's "Your Status" card reads.
   */
  readonly myRegistrationStatus: RegistrationStatus | null;
  readonly cancelledAt: string | null;
  readonly cancellationReason: string | null;
  readonly createdAt: string;
}

/**
 * What registering answers with.
 *
 * `status` is the whole point: one call covers RSVP and joining the waitlist,
 * and which one the member got is what they are waiting to be told.
 * `position` is their place in the queue when waitlisted.
 */
export interface RegistrationResult {
  readonly eventId: string;
  readonly status: RegistrationStatus;
  readonly position: number;
}

/**
 * What giving up a place answers with.
 *
 * `promotedMemberId` is whoever came off the waitlist because this place was
 * freed. The portal does not name them - it is somebody else's business - but
 * it does tell the member who left that their place went to someone.
 */
export interface CancelRegistrationResult {
  readonly eventId: string;
  readonly cancelled: boolean;
  readonly promotedMemberId: string | null;
}
