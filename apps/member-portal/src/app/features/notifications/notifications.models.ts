/**
 * Wire shapes for audit-notification-service, mirroring `NotificationResponse.cs`.
 *
 * The string unions are the names the service serialises its enums with,
 * checked against `NotificationEnums.cs` rather than guessed - the rule this app
 * learned the hard way (see `apps/member-portal/CLAUDE.md`).
 */

/** `NotificationChannel`. Only `InApp` ever reaches this screen - see below. */
export type NotificationChannel = 'InApp' | 'Email' | 'Sms' | 'WhatsApp';

/**
 * `NotificationStatus`. Delivery only: there is no `Read` here, because whether
 * a member has read something is a fact about that member and lives in a
 * separate table. A broadcast is one row a whole Samaaj shares, so a read flag
 * on it would have been set by the first person to open it.
 */
export type NotificationStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed';

export interface Notification {
  readonly id: string;
  readonly title: string;
  readonly body: string;
  readonly channel: NotificationChannel;
  readonly status: NotificationStatus;

  /** True when this went to the whole Samaaj rather than to this member. */
  readonly isBroadcast: boolean;
  readonly createdAt: string;

  /**
   * When *this* member read it, or null. Never "when anyone read it": the
   * service looks it up per member.
   */
  readonly readAt: string | null;
}

export interface MarkReadResult {
  readonly notificationId: string;
  readonly readAt: string;
  readonly alreadyRead: boolean;
}

export interface MarkAllReadResult {
  readonly markedRead: number;
}
