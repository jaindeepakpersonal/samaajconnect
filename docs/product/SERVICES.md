# Services

Eleven bounded-context services. Each follows the standard shape from the
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
- **Owns:** VolunteerGroup, GroupApplication, GroupMember, GroupRole,
  GroupElection, Vote
- **Key commands:** `CreateGroupCommand`, `ApplyToGroupCommand`,
  `DecideApplicationCommand`, `DefineGroupRoleCommand`,
  `AssignGroupRoleCommand`, `OpenElectionCommand`, `CastVoteCommand`,
  `PublishElectionResultsCommand`
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
  `UpdatePathshalaCommand` (name, address, contact person),
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

## 11. master-data-service

- **First / primary aggregate:** `LookupItem` (with `City`, `Locality`,
  and `Gautra` as supporting aggregates in the same service)
- **Owns:** LookupItem (one flat table for the `Country`,
  `ProfessionType`, `Gender` and `MaritalStatus` categories — see the
  service's own `CLAUDE.md` for why they share one shape), City,
  Locality, Gautra
- **Key commands:** `CreateLookupItemCommand`, `UpdateLookupItemCommand`,
  `DeactivateLookupItemCommand`, `CreateCityCommand`,
  `CreateLocalityCommand`, `CreateGautraCommand`, `UpdateGautraCommand`,
  `DeactivateGautraCommand`
- **Key queries:** `ListLookupItemsQuery`, `ListCitiesQuery`,
  `ListLocalitiesQuery`, `ListGautrasQuery` — all anonymous, since a
  registration form's dropdowns need these before the person filling
  them in has an account
- **Events published:** `master-data.lookup-item.*.v1`,
  `master-data.city.*.v1`, `master-data.locality.*.v1`,
  `master-data.gautra.*.v1` (created/updated/deactivated, twelve topics)
- **Events consumed:** none (source of master-data truth)
- **Gateway route prefix:** `/v1/master-data/**`, never module-gated —
  platform infrastructure, like identity and audit
- **Tenant role(s):** `SuperAdmin` (`MasterData.Manage` — the four
  platform-wide lookups and locations, shared by every Samaaj);
  `SamaajAdmin` (`Gautra.Manage` — their own Samaaj's Gautra list only)
- **Notes:** `Gautra` is the one tenant-scoped aggregate this service
  owns, per root `CLAUDE.md` §6's "Samaaj-wise" requirement — everything
  else here is platform-wide reference data. Every write is a soft
  delete (`IsActive = false`), never a hard delete: other services hold
  these rows' ids as plain foreign keys across a service boundary with
  no referential integrity to enforce it.

## 12. matrimony-service

- **First / primary aggregate:** `BiodataProfile` (`Id` is the member's
  own id), with `Proposal` as a second, peer-to-peer aggregate
- **Owns:** BiodataProfile, Proposal
- **Key commands:** `OptIntoMatrimonyCommand`, `UpdateBiodataCommand`,
  `WithdrawMatrimonyConsentCommand`, `SendProposalCommand`,
  `RespondToProposalCommand`, `WithdrawProposalCommand`
- **Key queries:** `GetMatrimonyConsentNoticeQuery`, `GetMyBiodataQuery`,
  `GetBiodataQuery`, `SearchBiodataQuery`, `ListSentProposalsQuery`,
  `ListReceivedProposalsQuery`
- **Events published:** `matrimony.biodata.created.v1`,
  `matrimony.consent.withdrawn.v1`, `matrimony.proposal.sent.v1`,
  `matrimony.proposal.responded.v1`, `matrimony.proposal.withdrawn.v1`
- **Events consumed:** `identity.user.erased.v1` — erases the member's
  biodata and de-identifies (not deletes) every proposal they were part
  of, so the other party's own history survives
- **Gateway route prefix:** `/v1/matrimony/**`, module-gated on
  `matrimony` (off by default, the same stance as `boli`)
- **Tenant role(s):** the same self-scoped role list
  member-family-service's own profile-update command uses, for every
  write; `Members.Read` for every read
- **Notes:** biodata's starting values (name, DOB, Gautra, profession,
  qualification, city, locality) are supplied by the client at opt-in
  time — a one-time copy from member-portal's own `GET /v1/members/me`
  call, never a live link back to the base profile, and never a
  synchronous call from this service to member-family-service. Visibility
  is within the caller's own Samaaj only; there is no cross-tenant search.

## 13. businesses-service

- **First / primary aggregate:** `Business` (a fresh id — a member may
  own more than one), with `Order` as a second, peer-to-peer aggregate
- **Owns:** Business (with `Product` as an owned entity), Order
- **Key commands:** `CreateBusinessCommand`, `UpdateBusinessCommand`,
  `SetBusinessStatusCommand`, `AddProductCommand`, `UpdateProductCommand`,
  `SetProductAvailabilityCommand`, `PlaceOrderCommand`,
  `RespondToOrderCommand`, `FulfillOrderCommand`, `CancelOrderCommand`
- **Key queries:** `ListMyBusinessesQuery`, `SearchBusinessesQuery`,
  `GetBusinessQuery`, `ListMyOrdersQuery`, `ListReceivedOrdersQuery`
- **Events published:** `businesses.business.created.v1`,
  `businesses.business.status-changed.v1`, `businesses.order.placed.v1`,
  `businesses.order.responded.v1`, `businesses.order.fulfilled.v1`,
  `businesses.order.cancelled.v1`
- **Events consumed:** `identity.user.erased.v1` — removes every business
  the member owned outright and de-identifies (not deletes) every order
  they placed as a buyer, so the business owner's own order history
  survives
- **Gateway route prefix:** `/v1/businesses/**`, module-gated on
  `businesses` (off by default, the same stance as `boli`/`matrimony`)
- **Tenant role(s):** the same self-scoped role list
  member-family-service's own profile-update command uses, for every
  write; `Members.Read` for every read
- **Notes:** `Business.CategoryId` is a master-data-service `LookupItem`
  id, `Category = BusinessCategory`. An order's line items and total are
  snapshotted at placement time, never read live off `Product` afterwards.
  Visibility is within the caller's own Samaaj only; there is no
  cross-tenant directory or order.

## 14. competition-service

- **First / primary aggregate:** `Competition` (owns registrations, judges
  and rounds), with `Entry`, `Score` and `RoundResult` as separate,
  non-owned aggregates
- **Owns:** Competition (with `CompetitionRegistration`, `CompetitionJudge`
  and `Round` as owned entities), Entry, Score, RoundResult
- **Key commands:** `CreateCompetitionCommand`, `MoveCompetitionCommand`,
  `RegisterForCompetitionCommand`, `WithdrawRegistrationCommand`,
  `AssignJudgeCommand`, `CreateRoundCommand`, `CloseRoundCommand`,
  `CreateEntryCommand`, `RecordScoreCommand`, `PublishRoundResultsCommand`
- **Key queries:** `ListCompetitionsQuery`, `GetCompetitionQuery`,
  `GetRoundQuery`, `GetRoundResultQuery`
- **Events published:** `competition.competition.status-changed.v1`,
  `competition.registration.created.v1`, `competition.round.closed.v1`,
  `competition.round.results-published.v1`
- **Events consumed:** none — a leaf service
- **Gateway route prefix:** `/v1/competitions/**`, module-gated on
  `competitions` (off by default, the same stance as `boli`/`matrimony`/
  `businesses`)
- **Tenant role(s):** new permission `Competition.Manage`, granted to
  `SamaajAdmin`, for every organiser command; `Members.Read` for
  registering, withdrawing, scoring as a judge, and every read
- **Notes:** `Competition.CompetitionTypeId` is a master-data-service
  `LookupItem` id, `Category = CompetitionType`. There is no automatic
  advancement between rounds — the organiser creates one `Entry` per
  round, by hand. A `Score` is an upsert while its round is open, unlike
  celebrity-voting-service's never-amended `Vote`. Individual judge scores
  are never shown to an ordinary member, before or after publication —
  only the published ranking (rank and average) is. Standalone: no
  dependency on events-service, and no change to it.

---

## 15. panch-service

- **First / primary aggregate:** `PanchElection` (owns its candidates),
  with `Ballot`, `Vote`, `ElectionResult` and `PanelMember` as separate,
  non-owned aggregates
- **Owns:** PanchElection (with `Candidate`), Ballot, Vote,
  ElectionResult, PanelMember
- **Key commands:** `CreateElectionCommand`, `MoveElectionCommand`,
  `NominateCandidateCommand`, `DecideCandidateCommand`,
  `CastBallotCommand`, `PublishElectionResultsCommand`,
  `RemovePanelMemberCommand`
- **Key queries:** `ListElectionsQuery`, `GetElectionQuery`,
  `GetResultsQuery`, `GetPanelQuery`
- **Events published:** `panch.election.status-changed.v1`,
  `panch.panel.changed.v1`
- **Events consumed:** none
- **Gateway route prefix:** `/v1/panch/**`, module-gated on `panch` (off by
  default)
- **Tenant role(s):** new permission `Panch.Manage`, granted to
  `SamaajAdmin`, for every organiser command; `Members.Read` for
  nominating, voting and every read
- **Notes:** the Samaaj Panch is a ten-member jury elected from the
  Samaaj's own members. Each member casts one final ballot of up to ten
  candidates (no self-vote); the ten most-backed win, each with at least
  one vote. Publishing replaces the seated roster; there is no term.
  `panch.panel.changed.v1` carries the whole roster as member ids so that
  social-issues-service, which decides who may review an issue from it,
  can replace its own copy idempotently.

## 16. trust-service

- **First / primary aggregate:** `Trust` (with `TrustMember` and
  `TrustElection` as peer, not owned, aggregates — a roster change and an
  election status change each raise their own event)
- **Owns:** Trust, TrustMember, TrustElection (with `TrustElectionCandidate`
  as an owned entity), Vote, Donation, Expense, Collection
- **Key commands:** `CreateTrustCommand`, `ChangeTrustStatusCommand`,
  `AddTrustMemberCommand`, `UpdateTrustMemberCommand`,
  `RemoveTrustMemberCommand`, `OpenTrustElectionCommand`,
  `MoveTrustElectionCommand`, `NominateTrustCandidateCommand`,
  `DecideTrustCandidateCommand`, `CastTrustVoteCommand`,
  `PublishTrustElectionResultsCommand`, `RecordDonationCommand`,
  `MarkDonationCollectedCommand`, `RecordExpenseCommand`,
  `ApproveExpenseCommand`, `RecordCollectionPaymentCommand`
- **Key queries:** `ListTrustsQuery`, `GetTrustQuery`, `ListTrustMembersQuery`,
  `ListTrustElectionsQuery`, `GetTrustElectionQuery`, `ListDonationsQuery`,
  `ListExpensesQuery`, `ListCollectionsQuery`, `ListMyDuesQuery`
- **Events published:** `TrustCreated`, `TrustStatusChanged`,
  `TrustMemberAdded`, `TrustMemberUpdated`, `TrustMemberRemoved`,
  `TrustElectionStatusChanged`, `TrustElectionResultsPublished`,
  `DonationRecorded`, `DonationCollected`, `ExpenseRecorded`,
  `ExpenseApproved`, `CollectionDue`, `CollectionSettled`
- **Events consumed:** `boli.result.published.v1` — opens a `Due` Collection
  against the winner for the Trust of the occasion's locality, once per Boli
- **Gateway route prefix:** `/v1/trust/**`, module-gated on `trust` (off by
  default, the same stance as `boli`/`matrimony`/`panch`)
- **Tenant role(s):** new permission `Trust.Manage`, granted to `SamaajAdmin`,
  for every write except nominating/voting; `Members.Read` for every read
  and, for nominating/voting, gated further by being an active member of the
  Trust the election belongs to
- **Notes:** Phase 2 of a larger plan — locality-wise Trust management, one
  Trust per `(TenantId, LocalityId)`. Phase 1 shipped roster CRUD; phase 2
  adds elections for trustee positions, copying volunteer-groups-service's
  `GroupElection` shape rather than sharing code with it. Donation and
  expense management, and a payment-collection ledger against both donations
  and a future locality-wise Boli, are later phases, not built yet. See the
  service's own `CLAUDE.md`.

## Inter-service data access rule

Services do not query each other's databases directly. A service that
needs another service's data either (a) subscribes to that service's
published events and keeps a local read-optimized copy, or (b) makes a
synchronous call through the gateway like any other client, subject to
the same tenant/role checks. Never share a connection string across
service boundaries.
