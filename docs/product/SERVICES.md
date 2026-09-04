# Services

Ten bounded-context services. Each follows the standard shape from the
`new-microservice` skill (Clean Architecture: Api / Application / Domain
/ Infrastructure, five pipeline behaviors, Outbox → Kafka). When you
scaffold one, feed the skill the "First aggregate" and "Tenant role(s)"
columns below as its four required inputs.

Each service should own exactly one Postgres logical database
(`sangam_{name}` or `samaajconnect_{name}`, per your naming convention)
and expose its endpoints behind the gateway at the route prefix shown.

---

## 1. identity-tenant-service

- **First / primary aggregate:** `Tenant` (plus `User`, `Role`,
  `Permission` as supporting aggregates in the same service)
- **Owns:** Tenant, User, Role, Permission, RolePermission, UserRole
  (see `DATA-MODEL.md` §2)
- **Key commands:** `CreateTenantCommand`, `ActivateTenantCommand`,
  `RegisterMemberCommand`, `LoginCommand`, `AssignRoleCommand`
- **Key queries:** `GetTenantBySlugQuery`, `GetCurrentUserQuery`,
  `ListTenantsQuery` (Super Admin only)
- **Events published:** `TenantCreated`, `TenantStatusChanged`,
  `UserRegistered`, `UserLoggedIn`, `RoleAssigned`
- **Events consumed:** none (source of tenant truth)
- **Gateway route prefix:** `/v1/identity/**`
- **Tenant role(s) that can invoke first command:** `SuperAdmin` only
  (tenant creation); `Anonymous` for `RegisterMemberCommand`
- **Notes:** This service is the only one allowed to issue JWTs, and the
  `tenant_id` claim it puts in them is what every other service scopes
  by. It also serves the public Samaaj directory the registration form
  picks from, and the by-id lookup the gateway uses to confirm a Samaaj
  is still active. The platform runs on a single domain — there is no
  subdomain resolution step (root `CLAUDE.md` §6).

## 2. member-family-service

- **First / primary aggregate:** `MemberProfile` (plus `Family`,
  `ChildProfile`)
- **Owns:** MemberProfile, Family, FamilyMember, ChildProfile,
  ChildConversionRequest
- **Key commands:** `UpdateProfileCommand`, `CreateFamilyCommand`,
  `RequestJoinFamilyCommand`, `CreateChildProfileCommand`,
  `RequestChildConversionCommand`, `ApproveChildConversionCommand`
- **Key queries:** `SearchMembersQuery` (tenant-scoped directory),
  `GetFamilyQuery`, `ListEligibleConversionsQuery`
- **Events published:** `MemberProfileUpdated`, `FamilyCreated`,
  `ChildConversionApproved` *(triggers `identity-tenant-service` to
  create the new login via a consumed event or synchronous call — decide
  per your inter-service call policy)*
- **Events consumed:** `UserRegistered` (to create the initial
  MemberProfile)
- **Gateway route prefix:** `/v1/members/**`, `/v1/families/**`
- **Tenant role(s):** `Member` (self-service), `FamilyHead` (family
  management), `SamaajAdmin` (moderation/correction)

  "Correction" is a narrower thing than it reads as here, and the narrowing
  is deliberate. An administrator holding `Members.Write` corrects factual
  details — a misspelt name, a stale number — through
  `PATCH /v1/members/{id}/details`. They cannot change what a member shares
  with their Samaaj, nor whether that member appears in the directory. Both
  travel only on the member's own `PATCH /v1/members/{id}`, which is self only.
  See `services/member-family-service/CLAUDE.md` for why the two were split.

## 3. timeline-service

- **First / primary aggregate:** `TimelinePost`
- **Owns:** TimelinePost, PostMedia, PostComment, PostReaction,
  ModerationAction
- **Key commands:** `CreatePostCommand`, `ModeratePostCommand`
  (approve/reject/hide/restore), `AddCommentCommand`
- **Key queries:** `GetTenantFeedQuery`, `GetModerationQueueQuery`
- **Events published:** `PostSubmitted`, `PostModerated`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/timeline/**`
- **Tenant role(s):** `Member` (post/comment), `ContentModerator` /
  `SamaajAdmin` (moderate)

## 4. volunteer-groups-service

- **First / primary aggregate:** `VolunteerGroup`
- **Owns:** VolunteerGroup, GroupApplication, GroupMember
- **Key commands:** `CreateGroupCommand`, `ApplyToGroupCommand`,
  `DecideApplicationCommand`, `AssignRolePositionCommand`
- **Key queries:** `ListGroupsQuery`, `GetGroupApplicationsQuery`
- **Events published:** `GroupApplicationSubmitted`,
  `GroupApplicationDecided`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/volunteer-groups/**`
- **Tenant role(s):** `Member` (apply), `VolunteerGroupPresident`
  (decide/assign), `SamaajAdmin` (create/deactivate group)

## 5. events-service

- **First / primary aggregate:** `Event`
- **Owns:** Event, EventRegistration
- **Key commands:** `CreateEventCommand`, `PublishEventCommand`,
  `RegisterForEventCommand`, `CancelEventCommand`
- **Key queries:** `ListUpcomingEventsQuery`, `GetAttendeeListQuery`
- **Events published:** `EventPublished`, `EventRegistrationCreated`,
  `EventCapacityReached`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/events/**`
- **Tenant role(s):** `SamaajAdmin` / `VolunteerGroupPresident`
  (create/publish), `Member` (register/RSVP)

## 6. social-issues-service

- **First / primary aggregate:** `SocialIssue`
- **Owns:** SocialIssue, IssueAttachment, IssueStatusHistory
- **Key commands:** `SubmitIssueCommand`, `DecideIssueCommand`
  (approve/reject/request changes), `PublishIssueCommand`,
  `CloseIssueCommand`
- **Key queries:** `GetApprovalQueueQuery`, `GetIssueHistoryQuery`
- **Events published:** `IssueSubmitted`, `IssueStatusChanged`,
  `IssuePublished`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/social-issues/**`
- **Tenant role(s):** `Member` (submit), `SamaajAdmin` /
  `ContentModerator` (approve/reject/publish)

## 7. celebrity-voting-service

- **First / primary aggregate:** `VotingCampaign`
- **Owns:** VotingCampaign, Candidate, Vote, CampaignResult
- **Key commands:** `CreateCampaignCommand`, `NominateCandidateCommand`,
  `CastVoteCommand`, `CloseCampaignCommand`, `PublishResultsCommand`
- **Key queries:** `GetCampaignQuery`, `GetLiveTallyQuery` (respects
  `ResultsVisibility`), `GetResultsQuery`
- **Events published:** `CampaignClosed`, `ResultsPublished`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/celebrity-voting/**`
- **Tenant role(s):** `SamaajAdmin` (configure/close/publish), `Member`
  (nominate/vote, subject to eligibility rule)
- **Notes:** `CastVoteCommand` must use a Redis atomic lock or a unique
  DB constraint on `(CampaignId, VoterMemberId)` to prevent
  double-voting under concurrent requests — this is a correctness
  requirement, not just a nice-to-have, given expected vote-close
  traffic spikes.

## 8. pathshala-service

- **First / primary aggregate:** `Pathshala` (with `Class`,
  `StudentEnrollment`, `Attendance`, `Exam` as the operational
  sub-aggregates most classes/attendance code will touch)
- **Owns:** Pathshala, AcademicSession, Class, ClassSchedule,
  TeacherAssignment, StudentEnrollment, Attendance, Exam, ExamResult,
  ProgressRecord, PathshalaEvent
- **Key commands:** `CreatePathshalaCommand` (Super Admin only),
  `EnrollStudentCommand`, `MarkAttendanceCommand`,
  `RecordExamResultCommand`
- **Key queries:** `GetMyClassQuery`, `GetMyAttendanceQuery`,
  `GetMyExamsQuery`, `GetMyProgressQuery`
- **Events published:** `StudentEnrolled`, `ExamResultRecorded`
- **Events consumed:** `ChildConversionApproved` (no direct effect
  expected, but useful for eventual consistency checks on enrollment
  records tied to a now-converted child)
- **Gateway route prefix:** `/v1/pathshala/**`
- **Tenant role(s):** `SuperAdmin` (create Pathshala master record),
  `SamaajAdmin`/`PathshalaTeacher` (operate), `FamilyHead` (enroll
  child), `PathshalaStudent` (read-only "My..." views)

## 9. boli-service

- **First / primary aggregate:** `BoliOccasion` (with `Boli` as the
  aggregate most bid-handling code will touch)
- **Owns:** BoliOccasion, BoliType, Boli, Bid, BoliResult
- **Key commands:** `CreateOccasionCommand`, `OpenBoliCommand`,
  `PlaceBidCommand`, `CloseBoliCommand`, `RecordResultCommand`,
  `PublishResultCommand`
- **Key queries:** `GetActiveBoliQuery`, `GetBidHistoryQuery`,
  `GetPublishedResultsQuery`
- **Events published:** `BoliClosed`, `ResultPublished`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/boli/**`
- **Tenant role(s):** `BoliManager` (occasion/type/close/publish),
  `Member` (bid, subject to eligibility)
- **Notes:** `PublishResultCommand` must be idempotent and irreversible
  through the normal API — corrections need a distinct, audited
  correction workflow rather than allowing a second publish.

## 10. audit-notification-service (cross-cutting)

- **First / primary aggregate:** `AuditLog` (with `Notification` as a
  second aggregate in the same service, since both are pure event
  consumers with no cross-service queries of their own)
- **Owns:** AuditLog, Notification, NotificationTemplate
- **Key commands:** `RecordIntegrationEventCommand` (internal, raised by this
  service's own Kafka consumer, not mapped to any endpoint),
  `MarkNotificationReadCommand`, `MarkAllNotificationsReadCommand`,
  `BroadcastNotificationCommand`
- **Key queries:** `GetAuditLogQuery` (Super Admin / Samaaj Admin),
  `GetMyNotificationsQuery`, `ListBroadcastsQuery`
- **Events published:** `notifications.broadcast.sent.v1`, and it publishes it
  to itself - the consumer subscribes to every versioned topic, so an
  announcement to a whole Samaaj becomes an audit row with the administrator
  who sent it on it
- **Events consumed:** *every* domain event listed above — this service
  has an `EventHandlers/` folder per consumed event type, each handler
  writing one AuditLog row and optionally emitting a Notification.
- **Gateway route prefix:** `/v1/audit/**`, `/v1/notifications/**`
- **Tenant role(s):** `SuperAdmin`/`SamaajAdmin` (read audit log), all
  authenticated roles (read own notifications)
- **Outbound delivery:** in-app notifications are delivered by being
  written; anything else is queued on the `Notification` row and sent by
  `NotificationDispatcher` through an `INotificationChannel` adapter.
  **There is no provider yet** — the registered adapter writes to the log,
  so `Sent` means "handed to the channel", not "reached a person". See the
  service's own `CLAUDE.md`, section "Outbound delivery".
- **Notes:** This is a strong candidate for the "tenth module" the
  `new-microservice` skill refers to, if it hasn't been scaffolded yet —
  scaffold it early (Phase 1) rather than last, since every other
  service needs an Outbox consumer to publish to from day one.

---

## Inter-service data access rule

Services do not query each other's databases directly. A service that
needs another service's data either (a) subscribes to that service's
published events and keeps a local read-optimized copy, or (b) makes a
synchronous call through the gateway like any other client, subject to
the same tenant/role checks. Never share a connection string across
service boundaries.
