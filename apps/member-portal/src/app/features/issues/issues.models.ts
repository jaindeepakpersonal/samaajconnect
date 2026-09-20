/**
 * Wire shapes for social-issues-service, mirroring `IssueResponses.cs`.
 *
 * The string unions are the names the service serialises `IssueStatus` with,
 * checked against `SocialIssue.cs` rather than guessed - the rule this app
 * learned the hard way (see `apps/member-portal/CLAUDE.md`).
 */

/**
 * `IssueStatus` in the domain: seven states, in workflow order.
 *
 * `Approved` and `Published` are retired: a reviewer's approval now puts an
 * issue in `DecisionPending`, and the Samaaj Panch decides it and closes it.
 */
export type IssueStatus =
  | 'Draft'
  | 'Submitted'
  | 'UnderReview'
  | 'DecisionPending'
  | 'Rejected'
  | 'ChangesRequested'
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
   * buttons from the status here would be a second copy of the workflow table,
   * and the two would drift.
   */
  readonly availableTransitions: readonly IssueStatus[];

  /** True when this caller may record the Panch's decision on this issue now. */
  readonly canDecide: boolean;
  readonly createdAt: string;

  /** What the Panch decided. Null until it has. */
  readonly decision: string | null;

  /** The volunteer group the Panch handed monitoring to, if it did. */
  readonly monitoringGroupId: string | null;
  readonly decidedAt: string | null;
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
 * Whether the caller reviews issues, and whether this Samaaj has a Panch.
 *
 * Mirrors `ReviewAccessResponse`. `canReview` is the fallback-aware answer: a
 * seated Panch member, or - only while no Panch is seated - an administrator.
 */
export interface ReviewAccess {
  readonly canReview: boolean;
  readonly canSeeQueue: boolean;
  readonly panelSeated: boolean;
  readonly isPanelMember: boolean;
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
  DecisionPending: 'Put in front of the Panch',
  Rejected: 'Reject',
  ChangesRequested: 'Ask for changes',
  Closed: 'Close',
};

/** What each state is called when describing where an issue stands. */
export const StatusLabels: Readonly<Record<IssueStatus, string>> = {
  Draft: 'Draft',
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  DecisionPending: 'Decision pending',
  Rejected: 'Not accepted',
  ChangesRequested: 'Changes requested',
  Closed: 'Closed',
};

/**
 * The strip on "My Submissions": Submitted → Under Review → Decision pending →
 * Closed.
 *
 * Only the happy path. Rejected and ChangesRequested are not steps along it -
 * they are where an issue leaves it - so the screen says so in words instead
 * of drawing a step that was never reached. A Closed issue with no decision
 * (withdrawn) is also off the path, for the same reason.
 */
export const ProgressSteps: readonly IssueStatus[] = [
  'Submitted',
  'UnderReview',
  'DecisionPending',
  'Closed',
];
