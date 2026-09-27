/** Mirrors `CaptchaResponse`: shown as `data:image/png;base64,{imagePng}`. Single use, five minutes. */
export interface Captcha {
  readonly captchaId: string;
  readonly imagePng: string;
  readonly expiresAt: string;
}

/**
 * Mirrors `SignInResponse`: either signed in (`session`), or waiting for the
 * code sent to `codeSentTo`, to be verified with `challengeToken`.
 */
export interface SignInResult {
  readonly codeRequired: boolean;
  readonly session: LoginResult | null;
  readonly challengeToken: string | null;

  /** The address the code went to, masked: "ra***@example.com". */
  readonly codeSentTo: string | null;
  readonly challengeExpiresAt: string | null;

  /** Set when this device was just trusted - `DeviceTrustStore` keeps it. */
  readonly deviceToken: string | null;
  readonly deviceTrustedUntil: string | null;
}

/** Mirrors LoginResponse from identity-tenant-service: an open session. */
export interface LoginResult {
  readonly accessToken: string;
  readonly expiresAt: string;

  /**
   * Buys the next access token. Single-use: spending it returns a replacement,
   * and spending one twice ends the whole session, because two parties holding
   * one means one of them is not the member.
   */
  readonly refreshToken: string;
  readonly refreshTokenExpiresAt: string;
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

  /**
   * Set only for an Area Admin: the one locality their role is restricted to.
   * Display only - member-family-service enforces it from the token itself.
   */
  readonly areaLocalityId?: string | null;
}

/** Mirrors RefreshSessionResponse. The same shape as a login, by design. */
export interface RefreshResult {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly refreshToken: string;
  readonly refreshTokenExpiresAt: string;
  readonly userId: string;
  readonly tenantId: string;
  readonly tenantSlug: string;
  readonly fullName: string;
  readonly roles: readonly string[];
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

/**
 * Mirrors `ActivateAccountResponse`.
 *
 * No token: redeeming a code sets a first password, it does not sign anybody
 * in. The next step is the ordinary login, which is also the first proof the
 * password works.
 */
export interface ActivationResult {
  readonly userId: string;
  readonly tenantSlug: string;
  readonly fullName: string;
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

/** Mirrors ChangePasswordResponse. No token: the caller keeps the one they have. */
export interface ChangePasswordResult {
  readonly userId: string;
  readonly changedAt: string;
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
