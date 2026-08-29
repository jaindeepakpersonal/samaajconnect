/**
 * Wire shapes for social-issues-service, mirroring `IssueResponses.cs`.
 *
 * The string unions are the names the service serialises `IssueStatus` with,
 * checked against `SocialIssue.cs` rather than guessed - the rule this app
 * learned the hard way (see `apps/member-portal/CLAUDE.md`).
 */

/** `IssueStatus` in the domain: eight states, in workflow order. */
export type IssueStatus =
  | 'Draft'
  | 'Submitted'
  | 'UnderReview'
  | 'Approved'
  | 'Rejected'
  | 'ChangesRequested'
  | 'Published'
  | 'Closed';

export interface Issue {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly category: string;
  readonly locality: string | null;

  /**
   * An id, not a name - names live in member-family-service. `isMine` is the
   * part the screen actually branches on.
   */
  readonly submittedByMemberId: string;
  readonly status: IssueStatus;

  /** True when the asking member raised this one. */
  readonly isMine: boolean;

  /**
   * What this caller may move it to right now.
   *
   * **The screen renders its buttons from this and never from `status`.** The
   * service computes it from the same transition table the aggregate enforces,
   * so a button that appears is a move the server will accept. Deriving the
   * buttons from the status here would be a second copy of an eight-state
   * table, and the two would drift.
   */
  readonly availableTransitions: readonly IssueStatus[];
  readonly createdAt: string;
  readonly publishedAt: string | null;
}

/** One step in the issue's life. */
export interface IssueHistoryEntry {
  readonly fromStatus: IssueStatus | null;
  readonly toStatus: IssueStatus;
  readonly actorUserId: string;
  readonly reason: string | null;
  readonly createdAt: string;
}

/**
 * An issue with the record of how it got here.
 *
 * The history is what answers "why was mine sent back?", which is the whole
 * reason this screen has a detail view at all.
 */
export interface IssueDetail {
  readonly issue: Issue;
  readonly history: readonly IssueHistoryEntry[];
}

/**
 * The categories the service accepts.
 *
 * A closed list on the server (`SubmitIssueCommandValidator.Categories`), so
 * this mirrors it exactly. The wireframe's dropdown showed three; the service
 * has six.
 */
export const IssueCategories = [
  'Community',
  'Education',
  'Environment',
  'Health',
  'Safety',
  'Infrastructure',
] as const;

/**
 * The two moves the service refuses without a reason.
 *
 * Both are ones the author is owed an explanation for. Asking for it in the
 * form rather than letting the server reject the request is the difference
 * between a prompt and an error message.
 */
export const TransitionsNeedingReason: readonly IssueStatus[] = ['Rejected', 'ChangesRequested'];

/** What each move is called on a button, rather than its enum name. */
export const TransitionLabels: Readonly<Record<IssueStatus, string>> = {
  Draft: 'Move back to draft',
  Submitted: 'Submit for approval',
  UnderReview: 'Start reviewing',
  Approved: 'Approve',
  Rejected: 'Reject',
  ChangesRequested: 'Ask for changes',
  Published: 'Publish',
  Closed: 'Close',
};

/** What each state is called when describing where an issue stands. */
export const StatusLabels: Readonly<Record<IssueStatus, string>> = {
  Draft: 'Draft',
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  Approved: 'Approved',
  Rejected: 'Not accepted',
  ChangesRequested: 'Changes requested',
  Published: 'Published',
  Closed: 'Closed',
};

/**
 * The strip the wireframe draws on "My Submissions": Submitted → Under Review →
 * Approved → Published.
 *
 * Only the happy path. Rejected, ChangesRequested and Closed are not steps
 * along it - they are where an issue leaves it - so the screen says so in
 * words instead of drawing a step that was never reached.
 */
export const ProgressSteps: readonly IssueStatus[] = [
  'Submitted',
  'UnderReview',
  'Approved',
  'Published',
];
