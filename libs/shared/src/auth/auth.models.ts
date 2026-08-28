/** Mirrors LoginResponse from identity-tenant-service. */
export interface LoginResult {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly userId: string;
  readonly tenantId: string;
  /** Empty for a platform account, which has no Samaaj subdomain to go to. */
  readonly tenantSlug: string;
  readonly fullName: string;
  readonly roles: readonly string[];
}

/** Mirrors CurrentUserResponse from identity-tenant-service. */
export interface CurrentUser {
  readonly userId: string;
  readonly tenantId: string;
  readonly tenantSlug: string;
  readonly mobileOrEmail: string;
  readonly fullName: string;
  readonly status: string;
  readonly isContactVerified: boolean;
  readonly lastLoginAt: string | null;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

export interface RegisterRequest {
  readonly tenantSlug: string;
  readonly fullName: string;
  readonly mobileOrEmail: string;
  readonly password: string;

  /** Purposes the visitor actively agreed to. Never pre-filled. */
  readonly consentedPurposes: readonly string[];

  /** Which version of the notice they were shown (DPDP s.6(7)). */
  readonly noticeVersion: string;
}

export interface RegisterResult {
  readonly userId: string;
  readonly tenantId: string;
  readonly tenantSlug: string;
  readonly mobileOrEmail: string;
  readonly isContactVerified: boolean;
}

/** Mirrors TenantSummaryResponse - what the anonymous slug lookup returns. */
export interface TenantSummary {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly logoUrl: string | null;
  readonly status: string;
  readonly enabledModules: readonly string[];
}

/** One purpose in the consent notice (DPDP s.5). */
export interface ConsentNoticeItem {
  readonly purpose: string;
  readonly title: string;
  readonly description: string;
  readonly required: boolean;
}

export interface ConsentNotice {
  readonly version: string;
  readonly items: readonly ConsentNoticeItem[];
}
