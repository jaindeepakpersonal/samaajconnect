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
PathshalaStudent, ContentModerator, BoliManager)

**Permission** *(platform-level, seeded)* — `Id, Key` (e.g.
`Pathshala.Attendance.Write`, `SocialIssues.Approve` — see
`SECURITY-CHECKLIST.md` for the full key list)

**UserRole** — `UserId, RoleId, TenantScope (specific tenant or "all"
for Super Admin), AssignedAt`

## 3. Member & Family

**MemberProfile** — `Id (=UserId), TenantId, FullName, PhotoUrl, DOB,
Gender, Mobile, Email, Address, Locality, Profession, PrivacyLevel
(Public/SamaajOnly/Private), CreatedAt`

**Family** — `Id, TenantId, FamilyHeadUserId, FamilyCode (unique per
tenant, used for join requests), CreatedAt`

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
PresidentMemberId, Status (Active/Inactive), CreatedAt`

**GroupApplication** — `Id, GroupId, MemberId, Status
(Pending/Accepted/Rejected), DecidedBy, DecidedAt`

**GroupMember** — `Id, GroupId, MemberId, RolePosition, JoinedAt`

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
(Draft/Submitted/UnderReview/Approved/Rejected/ChangesRequested/Published/Closed),
CreatedAt`

**IssueAttachment** — `Id, IssueId, Url, ScanStatus`

**IssueStatusHistory** — `Id, IssueId, FromStatus, ToStatus, ActorUserId,
Reason, CreatedAt`

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

**TeacherAssignment** — `Id, ClassId, TeacherMemberId, AssignedAt`

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
Status (Upcoming/Active/Closed)`

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
