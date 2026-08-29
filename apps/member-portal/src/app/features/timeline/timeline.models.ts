/**
 * Wire shapes for timeline-service, mirroring `PostResponses.cs`.
 *
 * These live in the member portal rather than in `libs/shared` because only
 * this app calls these endpoints. A type moves to the shared library when both
 * apps need it, not in anticipation - the admin panel's moderation screen is
 * not built, and when it is, the queue types move then.
 */

/** Announcement or MemberPost. */
export type PostType = 'Announcement' | 'MemberPost';

/**
 * The names timeline-service serialises `PostStatus` with, exactly.
 *
 * `PendingReview`, not `Pending` - and the difference is not cosmetic. These
 * are compared as strings against a `string` field on the wire, so a wrong name
 * is not a type error: the comparison silently never matches, and a member's
 * own post renders as though it were already public. That is what happened, and
 * only opening the screen against a running service showed it.
 *
 * `Draft` is here for completeness. The portal never creates one - a post is
 * submitted or it is not - but the service can hold one, and leaving it out of
 * the union would make a `Draft` row a type error at the boundary rather than
 * something the screen can decide about.
 */
export type PostStatus = 'Draft' | 'PendingReview' | 'Approved' | 'Rejected' | 'Hidden';

export interface ReactionCount {
  readonly type: string;
  readonly count: number;
}

export interface Post {
  readonly id: string;

  /**
   * An id, not a name. Names live in member-family-service and the feed does
   * not fetch them - see the remarks on `PostResponse` for why. The screen
   * shows the author's relationship to the reader ("Your post") rather than a
   * name it cannot resolve.
   */
  readonly authorMemberId: string;
  readonly type: PostType;
  readonly title: string;
  readonly body: string;
  readonly status: PostStatus;
  readonly reportCount: number;
  readonly reactions: readonly ReactionCount[];

  /** What this member reacted with, if anything. */
  readonly myReaction: string | null;
  readonly commentCount: number;
  readonly createdAt: string;
  readonly moderatedAt: string | null;
}

export interface Comment {
  readonly id: string;
  readonly authorMemberId: string;
  readonly body: string;
  readonly createdAt: string;
}

export interface PostDetail {
  readonly post: Post;
  readonly comments: readonly Comment[];
}

/**
 * The reactions the platform has.
 *
 * `ReactionType` in timeline-service is a closed enum and the validator refuses
 * anything else, so this is not the portal's list to choose - it is the
 * platform's, and these three names are exactly it. An earlier version offered
 * Like, Pray and Celebrate on the theory that the service stored whatever
 * string it was given; two of the three were rejected with a 400 that only
 * showed up when the screen was opened against a running service.
 *
 * The labels are separate from the types so the wording can change without
 * changing what goes on the wire.
 */
export const Reactions = [
  { type: 'Appreciate', label: 'Appreciate' },
  { type: 'Support', label: 'Support' },
  { type: 'Celebrate', label: 'Celebrate' },
] as const;

export type ReactionType = (typeof Reactions)[number]['type'];

/**
 * What reporting a post answers with.
 *
 * A message, not the post. Reporting does not change what the reporter sees -
 * the post stays exactly where it was until a moderator decides - and the
 * message is what tells them the report landed.
 */
export interface ReportAcknowledgement {
  readonly postId: string;
  readonly message: string;
}
