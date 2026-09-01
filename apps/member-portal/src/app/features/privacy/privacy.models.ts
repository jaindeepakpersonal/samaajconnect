/**
 * Wire shapes for the DPDP rights surface.
 *
 * Three services answer here, because a member's data is deliberately spread
 * across identity, member-family and audit and none of them reaches
 * synchronously into the others. Assembling the copy is therefore the client's
 * job - see `privacy.api.ts`.
 */

/** One purpose from the consent notice, with the words the member was shown. */
export interface ConsentNoticeItem {
  readonly purpose: string;
  readonly title: string;
  readonly description: string;

  /**
   * A purpose the account cannot exist without.
   *
   * Withdrawing it is not refused as an error so much as it is meaningless:
   * erasing the account is what withdrawing membership means, and the screen
   * says so rather than offering a button that answers 409.
   */
  readonly required: boolean;
}

export interface ConsentNotice {
  readonly version: string;
  readonly items: readonly ConsentNoticeItem[];
}

/** Where one purpose stands now. The history is in the export, not here. */
export interface ConsentState {
  readonly purpose: string;
  readonly granted: boolean;
  readonly noticeVersion: string;
  readonly decidedAt: string;
}

/** One decision as recorded, which is what the export carries. */
export interface ConsentRecord {
  readonly purpose: string;
  readonly action: string;
  readonly noticeVersion: string;
  readonly source: string;
  readonly recordedAt: string;
}

export interface AccountData {
  readonly userId: string;
  readonly tenantId: string;
  readonly tenantSlug: string;
  readonly mobileOrEmail: string;
  readonly fullName: string;
  readonly status: string;
  readonly isContactVerified: boolean;
  readonly createdAt: string;
  readonly lastLoginAt: string | null;
  readonly roles: readonly string[];
}

/**
 * What identity-tenant-service holds, and the half of DPDP section 11 that is
 * not a data dump: `processingPurposes` says what the platform does with it, in
 * the words the notice used, and `heldElsewhere` names the services this export
 * deliberately does not reach into.
 */
export interface IdentityExport {
  readonly exportedAt: string;
  readonly service: string;
  readonly account: AccountData;
  readonly consentHistory: readonly ConsentRecord[];
  readonly currentConsents: readonly ConsentState[];
  readonly processingPurposes: readonly ConsentNoticeItem[];
  readonly heldElsewhere: readonly string[];
}

/**
 * The other two exports, kept opaque on purpose.
 *
 * This screen shows them to the member as a file and never renders their
 * fields, so typing them here would be a second copy of two service contracts
 * for no gain - and a copy that would silently go stale as those services grow
 * fields, which is exactly the failure a data export must not have.
 */
export type ServiceExport = Readonly<Record<string, unknown>> & {
  readonly exportedAt?: string;
  readonly service?: string;
};

/** Everything the platform holds, as one file. */
export interface FullExport {
  readonly exportedAt: string;
  readonly platform: string;
  readonly note: string;
  readonly services: readonly ServiceExport[];
}

/**
 * What erasing actually did.
 *
 * Both halves are shown. A member told only "done" has no way to know an audit
 * record survives, and DPDP section 8(7) permits retention required by other
 * law - so saying what is kept, and why, is part of honouring the right rather
 * than a caveat on it.
 */
export interface EraseResult {
  readonly userId: string;
  readonly erasedAt: string;
  readonly whatWasErased: readonly string[];
  readonly whatIsKeptAndWhy: readonly string[];
}
