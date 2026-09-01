/**
 * Wire shapes for the admin surface, mirroring identity-tenant-service's
 * responses. These live in this app rather than in `libs/shared` because only
 * the admin panel calls these endpoints - root `CLAUDE.md` §7 puts a type in
 * the shared library when both apps need it, not before.
 */

/** Mirrors `TenantResponse`. The Super Admin view, with contact details. */
export interface Tenant {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly domain: string | null;
  readonly logoUrl: string | null;
  readonly contactPerson: string | null;
  readonly contactEmail: string | null;
  readonly status: TenantStatus;
  readonly enabledModules: readonly string[];
  readonly createdAt: string;
  readonly grievanceContact: GrievanceContact | null;
}

export type TenantStatus = 'Active' | 'Inactive' | 'Archived';

export interface GrievanceContact {
  readonly name: string | null;
  readonly email: string | null;
  readonly phone: string | null;
}

/** One module a Samaaj can run, from `ModuleCatalog`. */
export interface ModuleDescriptor {
  readonly key: string;
  readonly label: string;
  readonly defaultOn: boolean;
}

export interface CreateTenantRequest {
  readonly name: string;
  readonly slug: string;
  readonly domain: string | null;
  readonly contactPerson: string | null;
  readonly contactEmail: string | null;
  readonly enabledModules: readonly string[];
}

/** Mirrors `RoleMatrixResponse`. */
export interface RoleMatrix {
  /** Column order for the matrix, sent once rather than repeated per role. */
  readonly permissions: readonly string[];
  readonly roles: readonly Role[];

  /**
   * Whether **this caller, in this scope** may edit — not whether the matrix is
   * editable in principle. An ordinary member may read it, and a screen told
   * the second would offer them controls the server refuses.
   */
  readonly editable: boolean;
  readonly editableNote: string;
}

export interface Role {
  readonly id: string;
  readonly name: string;
  readonly assignableToAdmins: boolean;
  readonly permissions: readonly string[];

  /**
   * False for SuperAdmin, which is platform administration rather than Samaaj
   * administration — and the role that has to stay able to repair a Samaaj
   * that has locked itself out. Sent per role so the screen disables that
   * column rather than discovering the refusal on submit.
   */
  readonly editable: boolean;
}

/** Mirrors `AdminUserResponse`. */
export interface AdminUser {
  readonly userId: string;
  readonly fullName: string;
  readonly mobileOrEmail: string;
  readonly status: string;
  readonly lastLoginAt: string | null;
  readonly roles: readonly string[];
}

export interface InviteAdminRequest {
  readonly fullName: string;
  readonly mobileOrEmail: string;
  readonly roles: readonly string[];
}

/**
 * The activation code is plaintext and comes back exactly once - only its hash
 * is stored, and there is no way to look it up again.
 */
export interface InviteAdminResult {
  readonly userId: string;
  readonly fullName: string;
  readonly mobileOrEmail: string;
  readonly roles: readonly string[];
  readonly activationCode: string;
  readonly codeExpiresAt: string;
}

export interface AssignRoleResult {
  readonly userId: string;
  readonly role: string;
  readonly granted: boolean;

  /** False when the account already held (or already lacked) the role. */
  readonly changed: boolean;
  readonly roles: readonly string[];
}

/** Mirrors audit-notification-service's `AuditLogResponse`. */
export interface AuditLogEntry {
  readonly id: string;
  readonly tenantId: string;
  readonly action: string;
  readonly entityName: string;
  readonly entityId: string | null;

  /** Null on a row whose actor was erased (DPDP s.12), and on system events. */
  readonly actorUserId: string | null;
  readonly topic: string;
  readonly eventType: string;
  readonly occurredAt: string;
  readonly recordedAt: string;
}

/** Mirrors member-family-service's `ConversionRequestResponse`. */
export interface ConversionRequest {
  readonly id: string;
  readonly childProfileId: string;
  readonly childFullName: string;

  /** The identifier the new account would be created with. */
  readonly mobileOrEmail: string;
  readonly status: string;
  readonly requestedAt: string;
  readonly decidedBy: string | null;
  readonly decidedAt: string | null;
  readonly decisionNote: string | null;
}

/** Mirrors `PendingActivationResponse`. */
export interface PendingActivation {
  readonly userId: string;
  readonly fullName: string;
  readonly mobileOrEmail: string;
  readonly createdAt: string;
  readonly hasUsableCode: boolean;
  readonly codeExpiresAt: string | null;
}

/**
 * Mirrors `ActivationCodeResponse`. `code` is plaintext and is shown exactly
 * once; only its hash is stored, so it cannot be looked up again. Re-issuing
 * kills the previous one.
 */
export interface ActivationCode {
  readonly userId: string;
  readonly mobileOrEmail: string;
  readonly fullName: string;
  readonly code: string;
  readonly expiresAt: string;
}
