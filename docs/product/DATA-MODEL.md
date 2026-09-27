# Data Model

Every entity below carries `TenantId` unless marked **(platform-level)**.
`TenantId` scoping is enforced server-side via an EF Core global query
filter in each service's `DbContext` — never rely on the client or the
subdomain alone (see `SECURITY-CHECKLIST.md`).

Field lists are the minimum needed to build against; extend per service
as real requirements surface. Ownership per service is in `SERVICES.md`.

## 1. Entity-relationship diagram

```mermaid
erDiagram
    TENANT ||--o{ USER : "has"
    TENANT ||--o{ FAMILY : "scopes"
    USER ||--o| MEMBER_PROFILE : "has"
    USER ||--o{ USER_ROLE : "assigned"
    ROLE ||--o{ USER_ROLE : "grants"
    ROLE ||--o{ ROLE_PERMISSION : "includes"
    PERMISSION ||--o{ ROLE_PERMISSION : "included in"

    FAMILY ||--o{ FAMILY_MEMBER : "has"
    FAMILY ||--o{ CHILD_PROFILE : "has"
    MEMBER_PROFILE ||--o| FAMILY_MEMBER : "is"
    CHILD_PROFILE ||--o| CHILD_CONVERSION_REQUEST : "may raise"
    CHILD_CONVERSION_REQUEST ||--o| USER : "creates on approval"

    MEMBER_PROFILE ||--o{ TIMELINE_POST : "authors"
    TIMELINE_POST ||--o{ POST_COMMENT : "has"
    TIMELINE_POST ||--o{ POST_REACTION : "has"
    TIMELINE_POST ||--o{ MODERATION_ACTION : "moderated by"

    VOLUNTEER_GROUP ||--o{ GROUP_APPLICATION : "receives"
    VOLUNTEER_GROUP ||--o{ GROUP_MEMBER : "has"
    MEMBER_PROFILE ||--o{ GROUP_APPLICATION : "submits"

    EVENT ||--o{ EVENT_REGISTRATION : "has"
    MEMBER_PROFILE ||--o{ EVENT_REGISTRATION : "registers"
    VOLUNTEER_GROUP ||--o{ EVENT : "may organize"

    MEMBER_PROFILE ||--o{ SOCIAL_ISSUE : "submits"
    SOCIAL_ISSUE ||--o{ ISSUE_ATTACHMENT : "has"
    SOCIAL_ISSUE ||--o{ ISSUE_STATUS_HISTORY : "tracked by"

    VOTING_CAMPAIGN ||--o{ CANDIDATE : "has"
    MEMBER_PROFILE ||--o{ CANDIDATE : "nominated as"
    CANDIDATE ||--o{ VOTE : "receives"
    MEMBER_PROFILE ||--o{ VOTE : "casts"
    VOTING_CAMPAIGN ||--o| CAMPAIGN_RESULT : "produces"

    PATHSHALA ||--o{ ACADEMIC_SESSION : "runs"
    PATHSHALA ||--o{ CLASS : "has"
    CLASS ||--o{ CLASS_SCHEDULE : "has"
    CLASS ||--o{ STUDENT_ENROLLMENT : "has"
    CHILD_PROFILE ||--o{ STUDENT_ENROLLMENT : "enrolled via"
    CLASS ||--o{ TEACHER_ASSIGNMENT : "has"
    MEMBER_PROFILE ||--o{ TEACHER_ASSIGNMENT : "assigned as teacher"
    STUDENT_ENROLLMENT ||--o{ ATTENDANCE : "has"
    CLASS ||--o{ EXAM : "has"
    EXAM ||--o{ EXAM_RESULT : "produces"
    STUDENT_ENROLLMENT ||--o{ EXAM_RESULT : "receives"
    STUDENT_ENROLLMENT ||--o{ PROGRESS_RECORD : "has"
    PATHSHALA ||--o{ PATHSHALA_EVENT : "hosts"

    BOLI_OCCASION ||--o{ BOLI_TYPE : "defines"
    BOLI_OCCASION ||--o{ BOLI : "opens"
    BOLI ||--o{ BID : "receives"
    MEMBER_PROFILE ||--o{ BID : "places"
    BOLI ||--o| BOLI_RESULT : "produces"

    TENANT ||--o{ AUDIT_LOG : "scopes"
    TENANT ||--o{ NOTIFICATION : "scopes"

    MEMBER_PROFILE ||--o| BIODATA_PROFILE : "opts into"
    BIODATA_PROFILE ||--o{ PROPOSAL : "sends"
    BIODATA_PROFILE ||--o{ PROPOSAL : "receives"

    MEMBER_PROFILE ||--o{ BUSINESS : "owns"
    BUSINESS ||--o{ PRODUCT : "lists"
    BUSINESS ||--o{ ORDER : "receives"
    MEMBER_PROFILE ||--o{ ORDER : "places"
```

## 2. Platform-level entities (Identity & Tenant)

**Tenant** *(platform-level)* — `Id, Name, Slug (unique), Domain, LogoUrl,
ContactPerson, ContactEmail, Status (Active/Inactive/Archived),
EnabledModules (json), CreatedAt`

**User** — `Id, TenantId, MobileOrEmail (unique per tenant),
PasswordHash, AuthMethod (Password/OTP), Status (Active/Suspended),
LastLoginAt, CreatedAt`

**Role** *(platform-level, seeded)* — `Id, Name` (SuperAdmin, SamaajAdmin,
Member, FamilyHead, VolunteerGroupPresident, PathshalaTeacher,
PathshalaStudent, ContentModerator, BoliManager, AreaAdmin)

**Permission** *(platform-level, seeded)* — `Id, Key` (e.g.
`Pathshala.Attendance.Write`, `SocialIssues.Approve` — see
`SECURITY-CHECKLIST.md` for the full key list)

**UserRole** — `UserId, RoleId, TenantScope (specific tenant or "all"
for Super Admin), LocalityId (set only on an AreaAdmin grant - the one
locality it is restricted to), AssignedAt`

## 3. Member & Family

**MemberProfile** — `Id (=UserId), TenantId, FullName, PhotoUrl, DOB,
Gender, Mobile, Email, Address, Locality, Profession, FieldPrivacy (a
Public/SamaajOnly/Private level per field, not one for the profile),
IsListedInDirectory, CreatedAt, UpdatedAt`

`IsListedInDirectory` is not a sixth privacy level and could not be one: a
member who marks every field Private is still in the directory under their
name, because a listing is a name. It hides them from the directory search and
from nothing else - a profile stays reachable by id, which is what group
applications, post authorship and household membership need.

**Family** — `Id, TenantId, FamilyAdminMemberId, FamilyHeadMemberId (nullable),
FamilyCode (unique per tenant, used for join requests), CreatedAt`. The
**admin** manages the household (code, join requests, children, conversions);
the **head** is its eldest member, a label the admin picks that grants nothing.

**FamilyMember** — `Id, FamilyId, MemberProfileId, Relationship
(Spouse/Parent/Sibling/Other), Status (Active/PendingJoinRequest)`

**ChildProfile** — `Id, TenantId, FamilyId, FullName, DOB, Gender,
Status (Minor/EligibleForConversion/Converted), PhotoUrl, CreatedAt`

**ChildConversionRequest** — `Id, ChildProfileId, RequestedAt,
Status (Pending/Approved/Rejected), CreatedUserId (nullable until
approved), DecidedBy, DecidedAt`

## 4. Timeline

**TimelinePost** — `Id, TenantId, AuthorMemberId, Type
(Announcement/MemberPost), Body, Status
(Draft/PendingReview/Approved/Rejected/Hidden), CreatedAt`

**PostMedia** — `Id, PostId, Url, MediaType, ScanStatus`

**PostComment** — `Id, PostId, AuthorMemberId, Body, CreatedAt`

**PostReaction** — `Id, PostId, MemberId, ReactionType`

**ModerationAction** — `Id, PostId, ActorUserId, Action
(Approve/Reject/Hide/Restore), Reason, CreatedAt`

## 5. Volunteer Groups

**VolunteerGroup** — `Id, TenantId, Name, Description, FocusArea,
PresidentMemberId (nullable - set later by an admin or a "President" election), Status (Active/Inactive), CreatedAt`

**GroupApplication** — `Id, GroupId, MemberId, Status
(Pending/Accepted/Rejected), DecidedBy, DecidedAt`

**GroupMember** — `Id, GroupId, MemberId, RoleId, JoinedAt`. Every member
holds exactly one `GroupRole`; Volunteer until elected to another.

**GroupRole** — `Id, GroupId, Title, Kind (Volunteer/President/Elected),
Seats (null for Volunteer), CreatedAt`. Volunteer and President are built into
every group; the group defines its Elected roles. Title unique per group.

**GroupElection** — `Id, TenantId, GroupId, RoleId, PositionTitle (the role's
title when opened), Seats (frozen from the role), NominationStartAt,
NominationEndAt, VotingStartAt, VotingEndAt, Status, WinnerMemberIds (uuid[],
ranked), PublishedAt, CreatedAt`. Owns `ElectionCandidate`.

**Vote** — `Id, ElectionId, CandidateId, VoterMemberId, Choice, CreatedAt`. A
ballot is up to `Seats` picks numbered from 1; unique on (election, voter,
choice) and (election, voter, candidate).

## 6. Events

**Event** — `Id, TenantId, Title, Description, StartAt, EndAt, Venue,
OrganizerType (SamaajAdmin/VolunteerGroup), OrganizerId,
RegistrationEnabled, Capacity (nullable), Status
(Draft/Published/Cancelled), CreatedAt`

**EventRegistration** — `Id, EventId, MemberId, Status
(Registered/Waitlisted/Cancelled), RegisteredAt`

## 7. Social Issues

**SocialIssue** — `Id, TenantId, Title, Description, Category,
Locality, SubmittedByMemberId, Status
(Draft/Submitted/UnderReview/DecisionPending/Rejected/ChangesRequested/Closed),
CreatedAt, Decision (nullable, <= 2000), MonitoringGroupId (nullable, a
volunteer-groups-service id, loose), DecidedAt (nullable), DecidedBy (nullable
member id)` — judged by the Samaaj Panch: a reviewer's approval moves an issue
to `DecisionPending`, and any seated Panch member records `Decision` and closes
it. `Approved`/`Published` retired 2026-09-20; existing rows migrated
(Approved → DecisionPending, Published → Closed).

**IssueAttachment** — `Id, IssueId, Url, ScanStatus`

**IssueStatusHistory** — `Id, IssueId, FromStatus, ToStatus, ActorUserId,
Reason, CreatedAt`

**PanelMember** (social-issues-service's own copy) — `Id, TenantId, MemberId` —
fed by `panch.panel.changed.v1`; see §16.

## 8. Celebrity Voting

**VotingCampaign** — `Id, TenantId, Title, Description,
NominationStartAt, NominationEndAt, VotingStartAt, VotingEndAt,
TopN (default 10), EligibilityRule, ResultsVisibility
(Live/HiddenUntilClose), Status
(Draft/NominationsOpen/VotingOpen/Closed/Published)`

**Candidate** — `Id, CampaignId, MemberId, Category, NominatedBy,
Status (Nominated/Approved)`

**Vote** — `Id, CampaignId, CandidateId, VoterMemberId, CreatedAt` —
unique constraint on `(CampaignId, VoterMemberId)` per configured vote
rule to prevent duplicate voting.

**CampaignResult** — `Id, CampaignId, RankedCandidateIds (ordered json),
PublishedBy, PublishedAt`

## 9. Jain Pathshala

**Pathshala** *(created by Super Admin only)* — `Id, TenantId, Name,
Address, ContactPerson, Status, CreatedAt`

**AcademicSession** — `Id, PathshalaId, Label (e.g. "2026-27"),
StartDate, EndDate, IsCurrent`

**Class** — `Id, PathshalaId, SessionId, Name, RoomLabel`

**ClassSchedule** — `Id, ClassId, DayOfWeek, StartTime, EndTime`

**Teacher** — `Id, PathshalaId, FullName, Phone, Email, Qualification, UserId (nullable login), AddedAt`. Need not be a member.

**TeacherAssignment** — `Id, ClassId, TeacherId, AssignedAt`

**PathshalaDonation** — `Id, TenantId, PathshalaId, ReceiptNumber (unique per Pathshala), DonorName, DonorMemberId, Amount (paise), DonatedOn, Mode, Purpose, RecordedBy, RecordedAt, VoidedAt, VoidedBy, VoidReason`

**PathshalaAnnouncement** — `Id, TenantId, PathshalaId, Title, Body, SentBy, SentAt, RecipientCount`

**StudentEnrollment** — `Id, ClassId, ChildProfileId, SessionId,
EnrolledAt, Status (Active/Withdrawn)`

**Attendance** — `Id, EnrollmentId, ClassDate, Status
(Present/Absent/Excused)`

**Exam** — `Id, ClassId, Title, ExamDate, MaxScore`

**ExamResult** — `Id, ExamId, EnrollmentId, Score, Grade`

**ProgressRecord** — `Id, EnrollmentId, SessionId, AttendancePct,
AverageScore, ParticipationNotes, UpdatedAt`

**PathshalaEvent** — `Id, PathshalaId, Title, EventDate, Venue,
Description`

## 10. Auctions / Boli

**BoliOccasion** — `Id, TenantId, Title, Description, OccasionDate,
Status (Upcoming/Active/Closed), LocalityId (nullable)` — `LocalityId` is a
loose reference into master-data-service's `Locality`, with no referential
integrity across the service boundary, the same convention `Business
.LocalityId` and `Trust.LocalityId` already use. Null is a permanent,
legitimate state for a Samaaj too small to split its Boli by locality; it is
a tag/filter only so far — `Boli.Manage`/`Boli.PublishResults` are still
Samaaj-wide permissions, not gated by locality yet.

**BoliType** — `Id, OccasionId, Name, Description`

**Boli** — `Id, OccasionId, BoliTypeId, Title, StartAt, EndAt,
EligibilityRule, Status (Scheduled/Open/Closed/ResultPublished)`

**Bid** — `Id, BoliId, MemberId, Amount, PlacedAt` — validated against
current highest bid and Boli status server-side.

**BoliResult** — `Id, BoliId, WinningBidId, RecordedBy, RecordedAt,
PublishedBy (nullable), PublishedAt (nullable), Status
(RecordedNotPublished/Published)`

## 11. Cross-cutting (Audit & Notification)

**AuditLog** — `Id, TenantId, ActorUserId, ActorRole, Action,
EntityName, EntityId, BeforeState (json), AfterState (json),
IpAddress, CreatedAt` — immutable, append-only.

**Notification** — `Id, TenantId, RecipientUserId (nullable for
broadcast), Title, Body, Channel (InApp/Email/SMS/WhatsApp), Status
(Pending/Sending/Sent/Failed), Destination, DeliveryAttempts, LastAttemptAt,
DeliveredAt, FailureReason, DeliveryClaimId, SourceMessageId, CreatedAt`

`Status` is delivery only. It had a `Read` value, and that could not express a
broadcast: one row with no recipient, shared by a whole Samaaj, so the first
member to open it marked it read for everybody.

**NotificationRead** — `Id, NotificationId, UserId, TenantId, ReadAt`. Unique on
`(NotificationId, UserId)`. One member having read one message; the only place
read-ness can live, because it is a fact about a person and a message rather
than about a message.

**NotificationTemplate** — `Id, TenantId (nullable = platform default),
Key, Subject, Body, Channel`

## 12. Master data

**LookupItem** — `Id, Category (Country/ProfessionType/Gender/MaritalStatus),
Code, Label, SortOrder, IsActive` — one flat, admin-managed table for four
platform-wide reference lists, unique on `(Category, Code)`. Not
tenant-scoped: this is reference data every Samaaj shares.

**City** — `Id, CountryId (a LookupItem where Category == Country), Name,
IsActive` — not tenant-scoped.

**Locality** — `Id, CityId, Name, IsActive` — not tenant-scoped. This is the
"locations master"; the member-portal wireframe's existing locality dropdown
(Hiran Magri, Surajpole, Shobhagpura) is populated from here.

**Gautra** — `Id, TenantId, Name, IsActive` — **the one tenant-scoped entity
in this service.** A Samaaj's own Gautra (clan/lineage) list, per root
`CLAUDE.md` §6's "Samaaj-wise" requirement. Unique on `(TenantId, Name)`.

Every write across all four is a soft delete (`IsActive = false`); see
`services/master-data-service/CLAUDE.md` for why a hard delete is unsafe here
specifically — other services hold these rows' ids as plain foreign keys with
no cross-service referential integrity to enforce them.

## 13. Matrimony

**BiodataProfile** — `Id (=MemberId), TenantId, FullName, Gender, DateOfBirth,
GautraId, ProfessionId, QualificationId, CityId, LocalityId, HeightCm,
MaritalStatusId, Diet (enum), FamilyType (enum), FatherOccupation,
MotherOccupation, SiblingsCount, Manglik, HoroscopeNotes, AboutMe,
PreferredAgeMin, PreferredAgeMax, PartnerPreferenceNotes, ConsentGivenAt,
ConsentNoticeVersion, ConsentAttestation, ConsentWithdrawnAt (nullable),
IsActive, CreatedAt, UpdatedAt` — `Id` is the member's own id, the same
"no dedupe table, the id is the identity" convention `MemberProfile` itself
uses. `FullName` through `LocalityId` are a one-time copy from the base
profile, captured at opt-in and never re-synced. `IsActive` gates search
visibility; withdrawing consent sets it false without touching any other
field, which is what lets a member's own proposal history keep meaning
something after they leave.

**Proposal** — `Id, TenantId, FromMemberId, ToMemberId, Status
(Pending/Accepted/Declined/Withdrawn), Message, SentAt, RespondedAt
(nullable)` — a peer-to-peer fact between two `BiodataProfile`s, not owned
by either one. At most one `Pending`-or-`Accepted` row may exist between the
same two members in either direction at a time.

Not tenant-crossing: every search, proposal and response is scoped to the
caller's own Samaaj, with no exception. Full account erasure
(`identity.user.erased.v1`) erases `BiodataProfile` outright and
de-identifies (strips the message from, and closes out if still `Pending`)
every `Proposal` the erased member was part of, keeping the row so the other
party's own history is not silently broken.

## 14. Businesses

**Business** — `Id, TenantId, OwnerMemberId, Name, CategoryId, CityId
(nullable), LocalityId (nullable), Description (nullable), ContactPhone
(nullable), ContactEmail (nullable), IsActive, CreatedAt, UpdatedAt` —
`Id` is a fresh id, not the owner's own: unlike Matrimony's `BiodataProfile`,
a member may own more than one business. `CategoryId` is a
master-data-service `LookupItem` id, `Category = BusinessCategory`.
`ContactPhone`/`ContactEmail` are plain fields the owner types in for this
listing, not a read of the member's own privacy-gated Mobile/Email.

**Product** — `Id, Name, Description (nullable), PriceInPaise, IsAvailable,
CreatedAt, UpdatedAt` — an owned entity of `Business`, not its own
aggregate. Never deleted, only toggled unavailable, so a past order still
names a real product.

**Order** — `Id, TenantId, BusinessId, BusinessOwnerMemberId,
BuyerMemberId, Status (Pending/Accepted/Declined/Fulfilled/Cancelled),
Items (owned collection: ProductId, ProductName, UnitPriceInPaise,
Quantity — snapshotted at placement, never read live off `Product`
afterwards), TotalAmountInPaise (computed server-side from the snapshot),
Note (nullable), PlacedAt, RespondedAt (nullable), ClosedAt (nullable)` —
a peer-to-peer fact between a buyer and a `Business`, not owned by either.
`BusinessOwnerMemberId` is itself a snapshot, taken at placement, which is
what lets the aggregate enforce its own owner-only guards without a live
join back to `Business`.

Not tenant-crossing: every directory search and every order is scoped to
the caller's own Samaaj, with no exception. Full account erasure
(`identity.user.erased.v1`) removes every `Business` the erased member
owned outright (no other party has a stake in the row itself, only in the
`Order`s referencing it by plain id) and de-identifies (strips `Note`)
every `Order` they placed as a buyer, keeping the row so the business
owner's own order history is not silently broken.

## 15. Competitions

**Competition** — `Id, TenantId, Title, Description (nullable),
CompetitionTypeId (nullable, a master-data-service `LookupItem` id,
`Category = CompetitionType`), RegistrationStartAt, RegistrationEndAt,
Status (Draft/RegistrationOpen/RegistrationClosed/InProgress/Closed —
strictly forward), CreatedByMemberId, CreatedAt` — owns
`CompetitionRegistration`, `CompetitionJudge` and `Round` (all bounded, few
per competition). Standalone: no reference to events-service.

**CompetitionRegistration** — `Id, CompetitionId, MemberId, Status
(Registered/Withdrawn), RegisteredAt, WithdrawnAt (nullable)` — one row per
member per competition; withdrawing then registering again reactivates the
same row rather than inserting a second one.

**CompetitionJudge** — `Id, CompetitionId, MemberId, AssignedAt` — a member
the organiser named. Grants no platform role, the same "the president is
just a member" shape volunteer-groups-service's own group lead already
takes.

**Round** — `Id, CompetitionId, Name, SequenceNumber, Status
(Open/Closed), CreatedAt, ClosedAt (nullable)` — no automatic advancement
between rounds; the organiser creates entries for a round by hand, in every
round including the first.

**Entry** — `Id, TenantId, RoundId, ParticipantMemberId, CreatedAt` — a
separate, tenant-scoped aggregate, not owned by `Competition` or `Round`,
for the same "don't let the busy child balloon the parent" reasoning that
keeps `Order` its own aggregate rather than nested under `Business`.

**Score** — `Id, TenantId, EntryId, JudgeMemberId, Value (0-100), Comments
(nullable), CreatedAt, UpdatedAt` — also separate and never loaded by a
parent, the same reasoning as `Vote`/`Bid`, but an **upsert** while its
round is open rather than a never-amended record: correcting a misrecorded
mark before judging closes is the ordinary case here.

**RoundResult** — `Id, TenantId, RoundId, RankedEntryIds (ordered, jsonb),
PublishedBy, PublishedAt` — the frozen ranking, stored rather than
recomputed, mirroring celebrity-voting-service's own `CampaignResult`.

Not tenant-crossing: every read and write is scoped to the caller's own
Samaaj. Individual judge marks are never shown to an ordinary member,
before or after publication — only the published ranking's rank and
average are. A leaf service: it consumes nothing and holds no copy of
another service's data, so account erasure touches nothing here beyond
what the platform's own tenant isolation already scopes away.

A competition carries an optional `LocalityId` (2026-09-27), a loose reference
into master-data-service's Locality - null for the whole Samaaj. It bounds
nobody; it is what the admin panel's locality filter reads.

## 16. Samaaj Panch

**PanchElection** — `Id, TenantId, Title, Description (nullable),
NominationStartAt, NominationEndAt, VotingStartAt, VotingEndAt,
ResultsVisibility (Live/HiddenUntilClose), Status (Draft/NominationsOpen/
VotingOpen/Closed/Published — strictly forward), CreatedAt` — owns its
`Candidate` rows. Seats are fixed at ten.

**Candidate** — `Id, ElectionId, MemberId, NominatedByMemberId, Status
(Nominated/Approved/Removed), NominatedAt` — unique on `(ElectionId,
MemberId)`: one candidacy per member, or their votes would split.

**Ballot** — `Id, TenantId, ElectionId, VoterMemberId, CastAt` — unique on
`(ElectionId, VoterMemberId)`. This index is the one-ballot-per-member
guarantee.

**Vote** — `Id, TenantId, ElectionId, CandidateId, VoterMemberId` — one
row per choice on a ballot (at most ten), unique on `(ElectionId,
VoterMemberId, CandidateId)`. Inserted with its ballot in one transaction.

**ElectionResult** — `Id, TenantId, ElectionId (unique), RankedCandidateIds
(jsonb), SeatedMemberIds (jsonb), PublishedBy, PublishedAt` — the frozen
ranking; computed once.

**PanelMember** — `Id, TenantId, MemberId, ElectionId (nullable),
SeatedAt` — the seated roster, unique on `(TenantId, MemberId)`. Replaced
wholesale when an election is published; one row is removed when an
administrator unseats a member.

social-issues-service keeps its own copy of the roster, fed by
`panch.panel.changed.v1` (see SERVICES.md §15); it never queries this
service.

## 17. Trust (locality-wise Trust management, phase 2)

Phase 1 shipped Trust + roster CRUD; phase 2 added elections for trustee
positions; phase 3 adds the Trust's money - donations, expenses and a
collections ledger (below, after the election entities).

**Trust** — `Id, TenantId, LocalityId, Name, Status (Active/Inactive),
CreatedAt` — unique on `(TenantId, LocalityId)`: exactly one Trust per
locality. `LocalityId` is a loose reference into master-data-service's
`Locality`, with no referential integrity across the service boundary — the
same convention `Business.LocalityId` and `BiodataProfile.LocalityId`
already use.

**TrustMember** — `Id, TenantId, TrustId, MemberId, PositionTitle (nullable
free text — grants nothing), Status (Active/Removed), AddedBy, AddedAt,
RemovedBy (nullable), RemovedAt (nullable)` — its own aggregate, not owned
by `Trust`, so a roster change raises its own event, the same reasoning
panch-service's `PanelMember` gives for standing apart from its election.
Unique on `(TrustId, MemberId)`: at most one row per pair ever — re-adding a
previously removed member reactivates that same row rather than inserting a
second one. `PositionTitle` is set by hand (phase 1) or installed by a
published `TrustElection` (phase 2) — both paths go through the same
`UpdatePosition` method, so there is one way a position is ever held.

**TrustElection** — `Id, TenantId, TrustId, PositionTitle, NominationStartAt,
NominationEndAt, VotingStartAt, VotingEndAt, Status
(Draft/NominationsOpen/VotingOpen/Closed/Published), CreatedAt, ClosedAt
(nullable), WinnerMemberId (nullable), PublishedAt (nullable)` — copies
volunteer-groups-service's `GroupElection` shape. Owns `TrustElectionCandidate`
(`Id, ElectionId, MemberId, NominatedBy, NominatedAt, Status
(Nominated/Approved), ApprovedBy (nullable), ApprovedAt (nullable)`; unique
on `(ElectionId, MemberId)`) and does not own `Vote` (`Id, ElectionId,
CandidateId, VoterMemberId, CreatedAt`; unique on `(ElectionId,
VoterMemberId)` — the double-voting guarantee, written directly rather than
through the aggregate, the same shape every election on this platform uses).
The electorate for nominating and voting is the Trust's own active
`TrustMember` roster, not the whole Samaaj.

**Donation** (`trust_donations`) — `Id, TenantId, TrustId, DonorMemberId
(nullable), DonorName (nullable), DonorPhone (nullable), Amount (paise),
Purpose, Mode (Cash/Cheque/Upi/BankTransfer), Status (Pledged/Collected),
ReceiptNumber (nullable until collected), RecordedBy, CreatedAt, CollectedBy,
CollectedAt` — unique on `(TrustId, ReceiptNumber)` where a number is set: a
receipt names exactly one donation. Numbers run from 1 per Trust, issued under
a transaction-scoped advisory lock.

**Expense** (`trust_expenses`) — `Id, TenantId, TrustId, Category,
Description, Amount (paise), Vendor (nullable), Status (Recorded/Approved),
RecordedBy, RecordedAt, ApprovedBy, ApprovedAt`.

**Collection** (`trust_collections`) — `Id, TenantId, TrustId, SourceType
(Donation/BoliResult), SourceId, PayerMemberId (nullable), PayerName (nullable -
a donor with no account), Amount (paise), Status (Due/Collected), Mode (nullable
free text), CollectedBy, CollectedAt, CreatedAt, LastRemindedAt (nullable)` -
every pledge opens one, owed by its donor — unique on `(SourceType, SourceId)`: one Collection per source,
which is what makes consuming `boli.result.published.v1` idempotent.
