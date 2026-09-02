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

/**
 * Mirrors `BroadcastResponse`. `readCount` is how many members have opened it,
 * which is the number the wireframe's "Delivered" column wanted to be: an
 * in-app announcement is delivered the moment the row exists, so "Delivered"
 * says nothing at all.
 */
export interface Broadcast {
  readonly id: string;
  readonly title: string;
  readonly body: string;
  readonly sentAt: string;
  readonly readCount: number;
}

/** Mirrors `BroadcastNotificationResult`. */
export interface BroadcastResult {
  readonly id: string;
  readonly sentAt: string;
}

/** `PostStatus` in timeline-service's domain. */
export type PostStatus = 'Draft' | 'PendingReview' | 'Approved' | 'Rejected' | 'Hidden';

/** `ModerationDecision`. The queue says which of these it will accept. */
export type ModerationDecision = 'Approve' | 'Reject' | 'Hide' | 'Restore';

/** Mirrors `PostResponse`. `authorMemberId` is an id; names live elsewhere. */
export interface TimelinePost {
  readonly id: string;
  readonly authorMemberId: string;
  readonly type: string;
  readonly title: string;
  readonly body: string;
  readonly status: PostStatus;
  readonly reportCount: number;
  readonly commentCount: number;
  readonly createdAt: string;
  readonly moderatedAt: string | null;
}

/** Mirrors `ModerationActionResponse`. */
export interface ModerationAction {
  readonly actorUserId: string;
  readonly action: string;
  readonly reason: string | null;
  readonly createdAt: string;
}

/**
 * Mirrors `ModerationQueueItem`.
 *
 * `availableDecisions` comes from `TimelinePost.AvailableDecisions` in the
 * domain. The screen renders these and derives nothing from the status, so a
 * state added to the domain cannot leave this panel offering the wrong buttons.
 */
export interface ModerationQueueEntry {
  readonly post: TimelinePost;
  readonly history: readonly ModerationAction[];
  readonly availableDecisions: readonly ModerationDecision[];
}

// ---- Pathshala ----------------------------------------------------------

/** Mirrors `SessionResponse`. */
export interface AcademicSession {
  readonly id: string;
  readonly label: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly isCurrent: boolean;
}

/** Mirrors `ScheduleSlotResponse`. */
export interface ScheduleSlot {
  readonly dayOfWeek: string;
  readonly startTime: string;
  readonly endTime: string;
}

/**
 * Mirrors `ClassResponse`. `teacherMemberIds` are ids: names live in
 * member-family-service, and the panel resolves them from the directory it
 * already loads rather than asking per row.
 */
export interface PathshalaClass {
  readonly id: string;
  readonly sessionId: string;
  readonly sessionLabel: string;
  readonly name: string;
  readonly roomLabel: string | null;
  readonly schedule: readonly ScheduleSlot[];
  readonly teacherMemberIds: readonly string[];
  readonly studentCount: number;
}

/** Mirrors `PathshalaResponse` — the directory card, counts rather than rosters. */
export interface Pathshala {
  readonly id: string;
  readonly name: string;
  readonly address: string | null;
  readonly contactPerson: string | null;
  readonly status: string;
  readonly currentSessionLabel: string | null;
  readonly currentSessionId: string | null;
  readonly classCount: number;
  readonly teacherCount: number;
  readonly acceptsEnrolments: boolean;
}

/** Mirrors `PathshalaDetailResponse`. */
export interface PathshalaDetail {
  readonly id: string;
  readonly name: string;
  readonly address: string | null;
  readonly contactPerson: string | null;
  readonly status: string;
  readonly acceptsEnrolments: boolean;
  readonly sessions: readonly AcademicSession[];
  readonly classes: readonly PathshalaClass[];
}

/** `AttendanceStatus` in the domain. Excused is not counted against a student. */
export type AttendanceStatus = 'Present' | 'Absent' | 'Excused';

/**
 * Mirrors `RegisterEntryResponse` — one mark in one class's register.
 *
 * Read before the register is edited, because re-marking a date amends what is
 * already there and leaves anything not re-sent alone. Without this the screen
 * would be asking a teacher to re-enter the whole class from memory to correct
 * one child.
 */
export interface RegisterEntry {
  readonly enrolmentId: string;
  readonly status: AttendanceStatus;
  readonly markedAt: string;
}

/** Mirrors `RecordedResultResponse`. */
export interface RecordedResult {
  readonly enrolmentId: string;
  readonly score: number;
  readonly grade: string | null;
  readonly recordedAt: string;
}

/** Mirrors `ClassExamResponse` — an exam with the marks recorded in it. */
export interface ClassExam {
  readonly id: string;
  readonly classId: string;
  readonly title: string;
  readonly examDate: string;
  readonly maxScore: number;
  readonly results: readonly RecordedResult[];
}

/** `EnrolmentStatus` in the domain. */
export type EnrolmentStatus = 'Requested' | 'Active' | 'Withdrawn';

/**
 * Mirrors `EnrolmentResponse`.
 *
 * `childProfileId` is an id and pathshala-service holds no names — it stores a
 * child by id and nothing else. `GET /v1/children/names` resolves the ones on
 * screen, which is why the placement queue makes two calls rather than one.
 */
export interface Enrolment {
  readonly id: string;
  readonly pathshalaId: string;
  readonly childProfileId: string;
  readonly classId: string | null;
  readonly className: string | null;
  readonly sessionId: string | null;
  readonly sessionLabel: string | null;
  readonly status: EnrolmentStatus;
  readonly requestedAt: string;
  readonly enrolledAt: string | null;
}
