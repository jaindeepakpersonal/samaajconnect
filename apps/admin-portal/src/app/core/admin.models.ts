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

/** Mirrors `UserStatusResponse`. `changed` is false for a repeated click. */
export interface UserStatusResult {
  readonly userId: string;
  readonly status: string;
  readonly changed: boolean;
}

export interface InviteAdminRequest {
  readonly fullName: string;
  readonly mobileOrEmail: string;
  readonly roles: readonly string[];

  /** Required exactly when `roles` is `['AreaAdmin']`, and refused otherwise. */
  readonly localityId?: string;
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
  readonly address: PathshalaAddress | null;
  readonly contactPerson: string | null;
  readonly status: string;
  readonly currentSessionLabel: string | null;
  readonly currentSessionId: string | null;
  readonly classCount: number;
  readonly teacherCount: number;
  readonly acceptsEnrolments: boolean;
}

/**
 * Mirrors `PathshalaAddressResponse`. Ids, not names - Country/State/City/
 * Locality names live in master-data-service, and this screen already holds
 * the lists it needs to resolve them (the same lists the Master Data screen's
 * own Cities/Localities tabs use).
 */
export interface PathshalaAddress {
  readonly line: string | null;
  readonly countryId: string | null;
  readonly stateId: string | null;
  readonly cityId: string | null;
  readonly localityId: string | null;
}

/** Mirrors `PathshalaDetailResponse`. */
export interface PathshalaDetail {
  readonly id: string;
  readonly name: string;
  readonly address: PathshalaAddress | null;
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

// ---- Boli ----------------------------------------------------------------

/**
 * Amounts below are in **paise**, as integers, exactly as boli-service holds
 * them. `formatRupees` and `parseRupees` in `libs/shared` are the only place
 * this app converts — a Boli is money the Samaaj announces and collects
 * against, and a floating-point rupee accumulates error that shows up as a
 * winning bid a rupee off what somebody actually offered.
 */

/** Forward-only: Upcoming → Active → Closed. The service refuses backwards. */
export type OccasionStatus = 'Upcoming' | 'Active' | 'Closed';

export type BoliStatus = 'Scheduled' | 'Open' | 'Closed' | 'ResultPublished';

/** Mirrors `OccasionResponse` — the list card. */
export interface Occasion {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly occasionDate: string;
  readonly status: OccasionStatus;
  readonly typeCount: number;
  readonly boliCount: number;
}

/** Mirrors `BoliTypeResponse`. Nobody bids on a type; it is a label reused. */
export interface BoliType {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
}

/** Mirrors `BoliResponse` — one item being bid for. */
export interface Boli {
  readonly id: string;
  readonly occasionId: string;
  readonly boliTypeId: string;
  readonly boliTypeName: string;
  readonly title: string;
  readonly startAt: string;
  readonly endAt: string;
  readonly startingAmount: number;
  readonly minIncrement: number;
  readonly autoExtendSeconds: number;
  readonly eligibilityRule: string | null;
  readonly status: BoliStatus;

  /** Taking bids right now: the status says so *and* the clock agrees. */
  readonly acceptsBids: boolean;
  readonly highestAmount: number | null;
  readonly minimumNextBid: number;
  readonly highestBidderIsMe: boolean;
  readonly bidCount: number;
}

/** Mirrors `OccasionDetailResponse`. */
export interface OccasionDetail {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly occasionDate: string;
  readonly status: OccasionStatus;
  readonly types: readonly BoliType[];
  readonly boli: readonly Boli[];
}

/**
 * Mirrors `PendingResultResponse` — a result recorded and not yet announced.
 *
 * **No winner, deliberately.** boli-service names the winner in exactly one
 * shape and only once `publishedAt` is set, for everybody including the manager
 * who recorded it. The amount is what identifies the winning bid, and the
 * winner is not something the publisher chooses — `RecordResultCommand` reads
 * the highest bid and takes no winner parameter.
 */
export interface PendingResult {
  readonly boliId: string;
  readonly boliTitle: string;
  readonly occasionId: string;
  readonly amount: number;
  readonly recordedBy: string;
  readonly recordedAt: string;
}

// ---- Events --------------------------------------------------------------

/** Draft until published; cancelled is terminal and cannot be republished. */
export type EventStatus = 'Draft' | 'Published' | 'Cancelled';

/** A Samaaj's own event, or one a volunteer group is holding. */
export type OrganizerType = 'Samaaj' | 'VolunteerGroup';

/** Mirrors `EventResponse`. */
export interface SamaajEvent {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly startAt: string;
  readonly endAt: string | null;
  readonly venue: string | null;

  /** A master-data-service LookupItem id (Category EventType), or null. */
  readonly eventTypeId: string | null;
  readonly organizerType: OrganizerType;

  /** The group's id when the organiser is one. Names live in another service. */
  readonly organizerId: string | null;
  readonly status: EventStatus;
  readonly registrationEnabled: boolean;

  /** Null means no limit, which is a different thing from a limit of zero. */
  readonly capacity: number | null;
  readonly registeredCount: number;
  readonly waitlistedCount: number;

  /** True only when there is a capacity and it has been reached. */
  readonly isFull: boolean;
  readonly myRegistrationStatus: string | null;
  readonly cancelledAt: string | null;
  readonly cancellationReason: string | null;
  readonly createdAt: string;
}

/**
 * Mirrors `AttendeeResponse` — an id and a status, no name.
 *
 * events-service holds no names, and an attendee list is a list of who is going
 * somewhere: not a thing it should hand out more of than it has to. The panel
 * resolves the ids against the directory it already loads, as it does for post
 * authors and Pathshala teachers.
 */
export interface Attendee {
  readonly memberId: string;
  readonly status: 'Registered' | 'Waitlisted' | 'Cancelled';
  readonly registeredAt: string;
}

/** The subset of a volunteer group this panel needs, to name an organiser. */
export interface OrganizerGroup {
  readonly id: string;
  readonly name: string;
}

// ---- Celebrity voting ----------------------------------------------------

/** Strictly forward. An election that can go backwards is not an election. */
export type CampaignStatus =
  | 'Draft'
  | 'NominationsOpen'
  | 'VotingOpen'
  | 'Closed'
  | 'Published';

/** Whether members see the running count while voting is open. */
export type ResultsVisibility = 'Live' | 'HiddenUntilClose';

/** Nominated is put forward; Approved is on the ballot. */
export type CandidateStatus = 'Nominated' | 'Approved';

/** Mirrors `CampaignResponse`. */
/** What creating or editing a campaign sends. */
export interface CampaignInput {
  readonly title: string;
  readonly description: string | null;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly topN: number;
  readonly resultsVisibility: ResultsVisibility;
}

export interface Campaign {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly topN: number;
  readonly resultsVisibility: ResultsVisibility;
  readonly status: CampaignStatus;

  /** Right now: the status says so *and* the clock agrees. */
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;
  readonly myVoteCandidateId: string | null;
  readonly candidateCount: number;
  readonly createdAt: string;
}

/**
 * Mirrors `CandidateResponse`.
 *
 * `votes` is null when the tally is not visible to the caller — a
 * HiddenUntilClose campaign still running, seen by a member. Null rather than
 * zero, because zero is a claim and the wrong one. An administrator sees the
 * count throughout, so on this panel it is null only if something is off.
 */
export interface Candidate {
  readonly id: string;
  readonly memberId: string;
  readonly category: string | null;
  readonly status: CandidateStatus;
  readonly nominatedBy: string;
  readonly votes: number | null;
}

/** Mirrors `CampaignDetailResponse`. */
export interface CampaignDetail {
  readonly campaign: Campaign;
  readonly candidates: readonly Candidate[];

  /**
   * False when this caller is being shown a ballot without counts. The screen
   * needs the difference between "no votes yet" and "you may not see them".
   */
  readonly tallyVisible: boolean;
}

/** One place in a frozen ranking. */
export interface ResultEntry {
  readonly rank: number;
  readonly candidateId: string;
  readonly memberId: string;
  readonly votes: number;
}

/**
 * Mirrors `CampaignResultResponse` — the ranking as it was announced.
 *
 * Stored, never recomputed: a result that moved after it was announced would be
 * worse than no result at all.
 */
export interface CampaignResult {
  readonly campaignId: string;
  readonly ranking: readonly ResultEntry[];
  readonly publishedBy: string;
  readonly publishedAt: string;
}

// ---- Samaaj Panch --------------------------------------------------------

/** Forward only, like a campaign; publishing is its own call, then `Published`. */
export type ElectionStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

/** What creating an election sends. */
export interface ElectionInput {
  readonly title: string;
  readonly description: string | null;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly resultsVisibility: ResultsVisibility;
}

/** Mirrors panch-service's `ElectionResponse`. */
export interface Election {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;

  /** The Panch is this many members, and a voter may back up to this many. */
  readonly seats: number;
  readonly resultsVisibility: ResultsVisibility;
  readonly status: ElectionStatus;
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;
  readonly myChoices: readonly string[] | null;
  readonly candidateCount: number;
  readonly createdAt: string;
}

/** Nominated is put forward; Approved is on the ballot. */
export interface ElectionCandidate {
  readonly id: string;
  readonly memberId: string;
  readonly status: CandidateStatus;
  readonly nominatedBy: string;
  readonly votes: number | null;
}

export interface ElectionDetail {
  readonly election: Election;
  readonly candidates: readonly ElectionCandidate[];
  readonly tallyVisible: boolean;
}

export interface ElectionResultEntry {
  readonly rank: number;
  readonly candidateId: string;
  readonly memberId: string;
  readonly votes: number;
}

/** The ranking as announced, and who took the seats. Stored, never recomputed. */
export interface ElectionResult {
  readonly electionId: string;
  readonly ranking: readonly ElectionResultEntry[];
  readonly seatedMemberIds: readonly string[];
  readonly publishedBy: string;
  readonly publishedAt: string;
}

export interface PanelMember {
  readonly memberId: string;
  readonly electionId: string;
  readonly seatedAt: string;
}

/** The seated Panch. Names are ids on the wire and resolved from the directory. */
export interface Panel {
  readonly members: readonly PanelMember[];
  readonly seats: number;
  readonly viewerIsMember: boolean;
}

// ---- Volunteer groups ----------------------------------------------------

/** Inactive keeps its members and its history; it takes no new applications. */
export type GroupStatus = 'Active' | 'Inactive';

/** Mirrors volunteer-groups-service's `GroupResponse`. */
export interface VolunteerGroup {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly focusArea: string | null;

  /** An id, or null until one is set. Names live in member-family-service and are resolved here. */
  readonly presidentMemberId: string | null;
  readonly status: GroupStatus;
  readonly memberCount: number;
}

// ---- Volunteer group elections -------------------------------------------

/**
 * A group as its own detail endpoint returns it: the list shape plus the two
 * flags the service computes for the asker.
 *
 * `iCanLead` is the one this screen branches on. It is the service's answer to
 * "may this caller run this group's elections right now" - the president, or a
 * Samaaj admin **only while the group has no president** - so the panel never
 * re-derives it from a role and a president id.
 */
export interface LedGroup extends VolunteerGroup {
  readonly iAmThePresident: boolean;
  readonly iCanLead: boolean;
}

export interface GroupRosterMember {
  readonly memberId: string;
  readonly rolePosition: string | null;
  readonly joinedAt: string;
}

/** Mirrors `GroupDetailResponse`. */
export interface GroupDetail {
  readonly group: LedGroup;
  readonly members: readonly GroupRosterMember[];
}

export type GroupElectionStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

/** Mirrors `ElectionResponse` in volunteer-groups-service. */
export interface GroupElection {
  readonly id: string;
  readonly groupId: string;
  readonly positionTitle: string;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly status: GroupElectionStatus;

  /** Right now: the status says so *and* the clock agrees. */
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;
  readonly candidateCount: number;
  readonly winnerMemberId: string | null;
  readonly publishedAt: string | null;
  readonly createdAt: string;
}

/** A group election's vote counts are never hidden - it has no results-visibility setting. */
export interface GroupElectionCandidate {
  readonly id: string;
  readonly memberId: string;
  readonly status: 'Nominated' | 'Approved';
  readonly nominatedBy: string;
  readonly votes: number;
}

export interface GroupElectionDetail {
  readonly election: GroupElection;
  readonly candidates: readonly GroupElectionCandidate[];
}

/** A request to join a group. Mirrors `ApplicationResponse`. */
export interface GroupApplication {
  readonly id: string;
  readonly memberId: string;
  readonly note: string | null;
  readonly status: 'Pending' | 'Accepted' | 'Rejected';
  readonly createdAt: string;
}

/** What opening an election sends. */
export interface GroupElectionInput {
  readonly positionTitle: string;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
}

// ---- Trust -----------------------------------------------------------------

export type TrustStatus = 'Active' | 'Inactive';

/** Mirrors trust-service's `TrustResponse`. */
export interface Trust {
  readonly id: string;
  readonly localityId: string;
  readonly name: string;
  readonly status: TrustStatus;
  readonly createdAt: string;
}

export type TrustMemberStatus = 'Active' | 'Removed';

/** One row on a Trust's roster. Mirrors `TrustMemberResponse`. */
export interface TrustMember {
  readonly id: string;
  readonly trustId: string;
  readonly memberId: string;
  readonly positionTitle: string | null;
  readonly status: TrustMemberStatus;
  readonly addedBy: string;
  readonly addedAt: string;
  readonly removedBy: string | null;
  readonly removedAt: string | null;
}

export type TrustElectionStatus = 'Draft' | 'NominationsOpen' | 'VotingOpen' | 'Closed' | 'Published';

/** Mirrors `TrustElectionResponse`. Trust has no single leader, so unlike
 * `GroupElection` there is no `iCanLead`/president split - every admin-side
 * election command stays behind the same `Trust.Manage` that governs the
 * roster. */
export interface TrustElection {
  readonly id: string;
  readonly trustId: string;
  readonly positionTitle: string;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
  readonly status: TrustElectionStatus;
  readonly acceptsNominations: boolean;
  readonly acceptsVotes: boolean;
  readonly myVoteCandidateId: string | null;
  readonly candidateCount: number;
  readonly winnerMemberId: string | null;
  readonly publishedAt: string | null;
  readonly createdAt: string;
}

/** A Trust election's vote counts are never hidden - it has no results-visibility setting. */
export interface TrustElectionCandidate {
  readonly id: string;
  readonly memberId: string;
  readonly status: 'Nominated' | 'Approved';
  readonly nominatedBy: string;
  readonly votes: number;
}

export interface TrustElectionDetail {
  readonly election: TrustElection;
  readonly candidates: readonly TrustElectionCandidate[];
}

/** What opening an election sends. */
export interface TrustElectionInput {
  readonly positionTitle: string;
  readonly nominationStartAt: string;
  readonly nominationEndAt: string;
  readonly votingStartAt: string;
  readonly votingEndAt: string;
}

// ---- Social issues -------------------------------------------------------

/**
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

/**
 * Mirrors `IssueResponse`.
 *
 * `availableTransitions` comes from the domain's transition table, given this
 * caller's permission. The screen renders exactly those and derives nothing
 * from the status — the same rule the moderation queue follows, and for the
 * same reason: a second copy of the workflow in the panel is a copy that can
 * drift. `availableTransitions` is empty for an administrator once a Samaaj
 * has seated a Panch — reviewing is the Panch's from that point, and this
 * screen becomes read-only for it rather than lying about what will be
 * accepted.
 */
export interface SocialIssue {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly category: string;
  readonly locality: string | null;
  readonly submittedByMemberId: string;
  readonly status: IssueStatus;
  readonly isMine: boolean;
  readonly availableTransitions: readonly IssueStatus[];
  readonly canDecide: boolean;
  readonly createdAt: string;
  readonly decision: string | null;
  readonly monitoringGroupId: string | null;
  readonly decidedAt: string | null;
}

/** Mirrors `ReviewAccessResponse`. */
export interface IssueReviewAccess {
  readonly canReview: boolean;
  readonly canSeeQueue: boolean;
  readonly panelSeated: boolean;
  readonly isPanelMember: boolean;
}

/**
 * Mirrors `MemberResponse`, which is what member-family-service returns to a
 * Samaaj administrator for anybody in their Samaaj.
 *
 * **Every field can be null and null means "not set", not "not shared".** For
 * an administrator the two collapse: `IsVisibleTo` lets a Samaaj admin past
 * every privacy level, precisely because correcting somebody's details is the
 * job. In the member portal the same shape has to be read the other way round,
 * which is why that app's directory says "Not shared" and this one does not.
 *
 * What is deliberately absent is the member's privacy settings and whether they
 * are listed. This screen cannot change either, so being shown them would be
 * being shown a control that does nothing.
 */
/**
 * One address - the free-text line plus the City and Locality ids picked
 * from master-data-service. Mirrors `AddressResponse`, reused for both
 * Permanent and Temporary.
 */
export interface AdminAddress {
  readonly line: string | null;
  readonly cityId: string | null;
  readonly localityId: string | null;
}

export interface AdminMember {
  readonly id: string;
  readonly fullName: string;
  readonly photoUrl: string | null;
  readonly gautraId: string | null;
  readonly dateOfBirth: string | null;
  readonly mobile: string | null;
  readonly email: string | null;
  readonly permanentAddress: AdminAddress;
  readonly temporaryAddress: AdminAddress | null;
  readonly professionId: string | null;
  readonly qualificationId: string | null;
  readonly gender: string;
}

/** The body of `PATCH /v1/members/{id}/details`. No privacy fields, by design. */
export interface MemberCorrection {
  readonly fullName: string;
  readonly dateOfBirth: string | null;
  readonly gender: string;
  readonly mobile: string | null;
  readonly email: string | null;
  readonly gautraId: string | null;
  readonly professionId: string | null;
  readonly qualificationId: string | null;
  readonly permanentAddress: AdminAddress;
  readonly temporaryAddress: AdminAddress | null;
}

// ---- Reports & Analytics ---------------------------------------------------

/** Mirrors `RegistrationSummaryResponse`. Window is fixed server-side: 30 days
 *  against the 30 days before that. */
export interface RegistrationSummary {
  readonly currentPeriodCount: number;
  readonly previousPeriodCount: number;

  /** Null when the previous period counted nothing - not zero, which would
   *  misreport growth from nothing as no growth at all. */
  readonly growthPercentage: number | null;
}

/** Mirrors `AttendanceTally`, tenant-wide rather than per-enrolment. */
export interface AttendanceSummary {
  readonly present: number;
  readonly absent: number;
  readonly excused: number;
  readonly recorded: number;

  /** Null when nothing has been marked yet - zero would be a claim. */
  readonly percentage: number | null;
}

// ---- Settings: Security policy ---------------------------------------------

/**
 * Mirrors `SecurityPolicyResponse` - a Samaaj's own overrides, raw and
 * nullable. Null means "using the platform default", not zero and not
 * "unset in a way that breaks anything" - each field can be cleared back to
 * the default independently of the others.
 */
export interface SecurityPolicy {
  readonly minPasswordLength: number | null;
  readonly otpLifetimeMinutes: number | null;
  readonly maxFailedLoginAttempts: number | null;
  readonly lockoutDurationMinutes: number | null;
  readonly refreshTokenLifetimeDays: number | null;
}

// ---- Master data ------------------------------------------------------------

/** The flat lists behind `LookupItem`. Code and category are fixed once created. */
export type LookupCategory =
  | 'Country'
  | 'ProfessionType'
  | 'Gender'
  | 'MaritalStatus'
  | 'Qualification'
  | 'BusinessCategory'
  | 'EventType';

/** Mirrors master-data-service's `LookupItemResponse`. Every write is a soft delete. */
export interface LookupItem {
  readonly id: string;
  readonly category: LookupCategory;
  readonly code: string;
  readonly label: string;
  readonly sortOrder: number;
  readonly isActive: boolean;
}

/** Mirrors `CityResponse`. Scoped to a country - `countryId` names a `LookupItem` of category `Country`. */
export interface City {
  readonly id: string;
  readonly countryId: string;

  /** Null means no state named - added 2026-09-12, after cities already existed. */
  readonly stateId: string | null;
  readonly name: string;
  readonly isActive: boolean;
}

/** Mirrors `StateResponse`. Scoped to a country, same shape as `City`. */
export interface State {
  readonly id: string;
  readonly countryId: string;
  readonly name: string;
  readonly isActive: boolean;
}

/** Mirrors `LocalityResponse`. Scoped to a city. */
export interface Locality {
  readonly id: string;
  readonly cityId: string;
  readonly name: string;
  readonly isActive: boolean;
}

/** Mirrors `GautraResponse` - the calling Samaaj's own list. */
export interface Gautra {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
}

// ---- Competitions ---------------------------------------------------------

/** Strictly forward. There is no route back. */
export type CompetitionStatus =
  | 'Draft'
  | 'RegistrationOpen'
  | 'RegistrationClosed'
  | 'InProgress'
  | 'Closed';

export type RoundStatus = 'Open' | 'Closed';

/** Mirrors `CompetitionResponse`. */
export interface Competition {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly competitionTypeId: string | null;
  readonly registrationStartAt: string;
  readonly registrationEndAt: string;
  readonly status: CompetitionStatus;

  /** Right now: the status says so *and* the clock agrees. */
  readonly acceptsRegistrations: boolean;
  readonly registrationCount: number;

  /** Null when this caller (the organiser, viewing as themselves) has never registered. */
  readonly myRegistrationStatus: string | null;
  readonly isJudge: boolean;
  readonly createdAt: string;
}

/** Mirrors `RoundResponse`. */
export interface Round {
  readonly id: string;
  readonly name: string;
  readonly sequenceNumber: number;
  readonly status: RoundStatus;
  readonly createdAt: string;
  readonly closedAt: string | null;
  readonly entryCount: number;
  readonly hasPublishedResult: boolean;
}

/** Mirrors `JudgeResponse`. */
export interface CompetitionJudge {
  readonly id: string;
  readonly memberId: string;
  readonly assignedAt: string;
}

/** Mirrors `CompetitionDetailResponse`. */
export interface CompetitionDetail {
  readonly competition: Competition;
  readonly rounds: readonly Round[];
  readonly judges: readonly CompetitionJudge[];

  /** Currently-registered member ids, in registration order - what an entry is created from. */
  readonly registeredMemberIds: readonly string[];

  /** Whether this caller holds `Competition.Manage` - the organiser screen should show its controls. */
  readonly canManage: boolean;
}

/**
 * Mirrors `EntryResponse`.
 *
 * `averageScore` and `myScore` are both null for a caller who is neither the
 * organiser nor an assigned judge - individual marks are never shown to an
 * ordinary member, before or after publication. On this panel they are null
 * only when nobody has scored the entry yet.
 */
export interface CompetitionEntry {
  readonly id: string;
  readonly participantMemberId: string;
  readonly createdAt: string;
  readonly averageScore: number | null;
  readonly myScore: number | null;
}

/** Mirrors `RoundDetailResponse`. */
export interface RoundDetail {
  readonly round: Round;
  readonly entries: readonly CompetitionEntry[];
  readonly canSeeScores: boolean;
}

/** One place in a round's frozen ranking. */
export interface CompetitionResultEntry {
  readonly rank: number;
  readonly entryId: string;
  readonly participantMemberId: string;
  readonly averageScore: number;
}

/**
 * Mirrors `RoundResultResponse` - the ranking as it was announced.
 *
 * Stored, never recomputed: a result that moved after it was announced would be
 * worse than no result at all.
 */
export interface CompetitionResult {
  readonly roundId: string;
  readonly ranking: readonly CompetitionResultEntry[];
  readonly publishedBy: string;
  readonly publishedAt: string;
}
