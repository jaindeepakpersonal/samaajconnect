# pathshala-service

## Purpose

Runs a Samaaj's Jain Pathshala: the school, its academic sessions and classes,
who teaches them, which children are enrolled, the register, and the exams.

Behind the `pathshala` module key, on by default.

Creating the master record is reserved to the platform (DATA-MODEL.md §9);
everything else is the Samaaj's to run.

## Entities

| Entity | Notes |
|---|---|
| `Pathshala` | Aggregate root. Owns its sessions and classes, and **not** its enrolments |
| `AcademicSession` | e.g. "2026-27". Exactly one per Pathshala is current |
| `PathshalaClass` | One class in a session, with its timetable and teachers |
| `ClassSchedule` | A weekly slot. Overlapping slots on one day are refused |
| `Teacher` | Owned by the Pathshala. Name, phone, email, qualification, and an optional linked login - need not be a member |
| `TeacherAssignment` | Which teacher record teaches a class. Unique per (class, teacher) |
| `PathshalaDonation` | Aggregate root. One entry in the donation ledger: paid (receipt-numbered) or announced and still to be paid (no receipt yet). A paid one is voided, never removed; an announcement is erased |
| `PathshalaAnnouncement` | Aggregate root. A message to the parents; its text stays here |
| `StudentEnrolment` | Aggregate root. One child's place: requested, then placed |
| `AttendanceEntry` | One mark. Written directly, unique per (enrolment, date) |
| `Exam` | Aggregate root. Set for a class |
| `ExamResult` | One mark. Written directly, unique per (exam, enrolment) |

Two entities in DATA-MODEL.md §9 are deliberately absent. See "Decisions".

## Commands

| Command | Policy | Notes |
|---|---|---|
| `CreatePathshalaCommand` | `SuperAdmin` + `Pathshala.Manage` | The one act reserved to the platform |
| `UpdatePathshalaCommand` | `Pathshala.Manage` | Name, address, contact person. An Area Admin cannot move it out of their locality |
| `AddTeacherCommand` | `Pathshala.Manage` | A teacher record - need not be a member. `userId` links their login, if any |
| `UpdateTeacherCommand` | `Pathshala.Manage` | Details or login. One login is one teacher per Pathshala |
| `RemoveTeacherCommand` | `Pathshala.Manage` | Takes them off every class; the registers they marked are kept |
| `RecordDonationCommand` | `Pathshala.Manage` | Paid: issues the next receipt number. `paid: false`: an announcement, with no receipt or mode |
| `UpdateDonationCommand` | `Pathshala.Manage` | Corrects an entry. A paid one needs its mode and paid date; a pledge stays a pledge. 409 once deleted |
| `MarkDonationPaidCommand` | `Pathshala.Manage` | An announcement was paid: issues the next receipt number. 409 once paid or deleted |
| `VoidDonationCommand` | `Pathshala.Manage` | The admin panel's Delete for a paid entry: off the ledger and out of the totals with a reason, the row kept. 409 on an announcement |
| `DeleteDonationCommand` | `Pathshala.Manage` | The admin panel's Delete for an announcement: erases the row. 409 once it has a receipt |
| `SendAnnouncementCommand` | `Members.Read` or `Pathshala.Teach` + a teacher of it or somebody who runs it | To the parents of every child with a place or a request |
| `OpenSessionCommand` | `Pathshala.Manage` | Makes the new session current |
| `CreateClassCommand` | `Pathshala.Manage` | |
| `AddClassSlotCommand` | `Pathshala.Manage` | Refuses an overlap |
| `AssignTeacherCommand` | `Pathshala.Manage` | Assign and remove in one command, by teacher record |
| `DeactivatePathshalaCommand` | `Pathshala.Manage` | Records are kept; enrolments stop |
| `RequestEnrolmentCommand` | `Members.Read` | A parent asks, giving a name and phone the Pathshala may reach them by |
| `PlaceStudentCommand` | `Pathshala.Manage` | The Pathshala decides, and picks the class |
| `WithdrawStudentCommand` | `Pathshala.Manage` | Off the roll; attendance and results stay |
| `MarkAttendanceCommand` | `Pathshala.Attendance.Write` **and** teaching this class | The whole register in one submission |
| `ScheduleExamCommand` | `Pathshala.Exams.Write` **and** teaching this class | |
| `RecordExamResultCommand` | `Pathshala.Exams.Write` **and** teaching this class | Re-recording amends |
| `ConsumeIntegrationEventCommand` | `[InternalRequest]` | Links a converted child's enrolments to their new account |

## Queries

| Query | Policy | Notes |
|---|---|---|
| `ListPathshalasQuery` | `Members.Read` or `Pathshala.Teach` | Counts, not rosters. `staffed` narrows to the Pathshalas the caller works at; forced for a caller without `Members.Read` |
| `GetPathshalaQuery` | `Members.Read` or `Pathshala.Teach` | Sessions and classes. `asStaff` answers "not found" unless the caller works here; forced for a caller without `Members.Read` |
| `ListEnrolmentRequestsQuery` | `Pathshala.Manage` | The placement queue |
| `GetAttendanceSummaryQuery` | `Pathshala.Manage` | Tenant-wide, unlike every other tally here |
| `GetClassRollQuery` | `Members.Read` or `Pathshala.Teach` + teaching this class | A list of somebody's children |
| `GetClassRegisterQuery` | `Members.Read` or `Pathshala.Teach` + teaching this class | One date's marks, so amending is not a guess |
| `ListClassExamsQuery` | `Members.Read` or `Pathshala.Teach` + teaching this class | The class's exams with their marks |
| `ListMyEnrolmentsQuery` | `Members.Read` | What this member asked for, or holds |
| `GetMyClassQuery` | `Members.Read` + owns this enrolment | |
| `GetMyAttendanceQuery` | `Members.Read` + owns this enrolment | |
| `GetMyExamsQuery` | `Members.Read` + owns this enrolment | |
| `GetMyProgressQuery` | `Members.Read` + owns this enrolment | Computed, not stored |
| `ListTeachersQuery` | `Members.Read` or `Pathshala.Teach` + a teacher of it or somebody who runs it | With contact details |
| `ListStudentsQuery` | `Members.Read` or `Pathshala.Teach` + staff | Placed children in the caller's classes, and (for administrators) waiting requests, with the parent's contact |
| `GetProgressReportQuery` | `Members.Read` or `Pathshala.Teach` + staff | Every placed child's attendance and exam average |
| `GetAttendanceOverviewQuery` | `Members.Read` or `Pathshala.Teach` + staff | Per class: days marked and the tally |
| `ListPathshalaExamsQuery` | `Members.Read` or `Pathshala.Teach` + staff | Every exam, with how far marking has got |
| `ListDonationsQuery` | `Pathshala.Manage` | The ledger, what was received and what is announced and not yet paid |
| `ListAnnouncementsQuery` | `Members.Read` or `Pathshala.Teach` + staff | What this Pathshala has sent |
| `ListMyAnnouncementsQuery` | `Members.Read` | From every Pathshala where the caller has a child with a place or a request |

## Events published

- `pathshala.created.v1`
- `pathshala.updated.v1` (ids only, like its creation)
- `pathshala.teacher.added.v1`, `.updated.v1`, `.removed.v1` (ids and who did it)
- `pathshala.donation.recorded.v1` (ids, amount, whether it is a pledge, who), `.paid.v1` (the receipt number, amount, who), `.updated.v1` and `.voided.v1` (ids and who), `.erased.v1` (ids, amount and who - never the donor, whose row is gone)
- `pathshala.announcement.sent.v1` (who, and how many) and one `pathshala.announcement.addressed.v1` per parent - see "Teachers, the staff tabs, donations and announcements"
- `pathshala.session.opened.v1`
- `pathshala.deactivated.v1`
- `pathshala.enrolment.requested.v1`
- `pathshala.student.enrolled.v1` (SERVICES.md's `StudentEnrolled`)
- `pathshala.exam-result.recorded.v1`

## Events consumed

`identity.child-conversion.completed.v1`.

**SERVICES.md names `members.child-conversion.approved.v1`, and that event
cannot do the job.** member-family-service publishes it when an admin approves
the conversion, before identity-tenant-service has created anything, so it
carries a child profile id and no user id — there is nothing to link an
enrolment to. The completed event carries both, and is published at the moment
the link becomes true.

## API endpoints

See `docs/product/API-CONTRACTS.md`, including where the shipped shape departs
from the requirements draft.

## Authorization

**A permission says what kind of person you are; it never says whose records.**
`Pathshala.Attendance.Write` means "a teacher"; it does not mean "a teacher of
this class", and treating it as though it did would let any teacher in the
Samaaj mark any register in any Pathshala. Every write and every
student-facing read therefore pairs its permission with a data check in
`PathshalaAccess` — the pattern volunteer-groups-service established with "are
you this group's president?".

**Nothing is gated on the `PathshalaStudent` role, and it must stay that way
until something can grant it.** The platform catalogue described it as "created
by enrolment"; enrolment happens here, and this service cannot write role
grants in identity-tenant-service. A permission held only by a role nobody has
is a permission nobody has — the fourth time that shape has bitten this repo,
after FamilyHead and VolunteerGroupPresident. The student views are gated on
`Members.Read`, which every signed-in member holds, and access is decided
against the enrolment: the parent who asked, the student once conversion gives
them an account, a teacher of their class, or an administrator.

**`Pathshala.Manage` was held by nobody.** Building this service is what
surfaced it: SamaajAdmin now holds it, and creating the master record is
reserved by a SuperAdmin *role* check on that one command instead. Reserving it
by withholding the permission would have left every other Pathshala operation
reachable by nobody but the platform operator.

**Refusals are "not found", not "forbidden", throughout.** A 403 on an
enrolment id confirms the enrolment exists, and these are records about
somebody's child.

**An Area Admin holds `Pathshala.Manage` for one locality.** The Pathshala's
own `Address.LocalityId` decides it, read against `IAreaScope` (the
`locality_id` token claim). `PathshalaAccess.CanAdminister` now takes the
Pathshala, so the administrator branch of `CanTeach` and `CanReadRecordsOf` -
reading a roll, a register, a child's records - is locality-bound too, and
`IsAdministeredBy` sits beside `IsInTenant` in every `Pathshala.Manage`
handler: opening sessions, classes, slots, teachers, standing one down, the
placement queue, placing, declining and withdrawing. Outside their locality,
or on a Pathshala with no address, each answers "not found" like everything
else here. The Samaaj-wide attendance figure refuses them outright (403): it
is a whole-Samaaj number, and there is no one Pathshala to scope it to.
Listing Pathshalas and a parent's own requests stay Samaaj-wide.

A Pathshala created before its locality mattered has none, and stays the
Samaaj Admin's until somebody gives it one with `UpdatePathshalaCommand`
(2026-09-26). That command is `Pathshala.Manage` alone - creating decides a
Pathshala exists, which is the platform's call, but a wrong address on one
that exists is the Samaaj's to put right. An Area Admin may correct their own
locality's Pathshala and not move it out: a blank locality is filled with
theirs, because clearing it would hand the Pathshala to the Samaaj Admin, and
naming another answers 403 - events-service's rule for an event.

## A teacher who is not a member

Added 2026-09-27. A teacher from outside the Samaaj has an account holding the
`PathshalaTeacher` role and no `Member` role, so it carries `Pathshala.Teach`
and not `Members.Read` (identity-tenant-service's catalogue). Every read a
teacher needs - the Pathshala and its tabs, a class's roll, register and exams,
sending an announcement - declares
`[RequiresPermission(MembersRead, PathshalaTeach)]`, either key opening the
door and `PathshalaAccess` still deciding which Pathshala. Everything a parent
does - asking for a place, their own child's records, their announcements - is
`Members.Read` alone, so such a teacher is refused it.

`PathshalaAccess.IsOutsider` is "no `Members.Read`". For an outsider the list is
always the Pathshalas they work at and any other Pathshala is "not found" - not
even the directory card a member reads. A member who teaches keeps the
directory, and the admin panel asks for the staff view (`?staffed=true`,
`?asStaff=true`) whenever the caller does not hold `Pathshala.Manage`.

## Teachers, the staff tabs, donations and announcements

Added 2026-09-26, for the admin panel's tabbed Pathshala page.

**A teacher is the Pathshala's own record, and need not be a member of the
Samaaj** (the user's requirement). `Teacher` (name, phone, email,
qualification) is owned by the `Pathshala` aggregate like its sessions and
classes, and a class's `TeacherAssignment` names a teacher record rather than a
member id. `Teacher.UserId` optionally links a login - a Samaaj member, or an
account given only the `PathshalaTeacher` role through Invite Admin - and it is
what "teaches this class" is decided against (`Pathshala.Teaches`). A teacher
without a login is a record, and an administrator marks and records for them
(confirmed with the user). One login is one teacher per Pathshala: the aggregate
refuses a second, and a filtered unique index holds it.

The migration gave every member who taught a class before this a teacher record
per Pathshala, linked to the same login so their registers keep working, named
"Teacher (add their details)" because names were never held here;
`TeacherRecordsMigrationTests` runs it against legacy rows.

**The staff tabs share one access rule.** Students, Progress report, Attendance
and Exams are "rows for the classes this caller may see", and
`PathshalaAccess.StaffScopeOf` is that answer: every class for somebody who runs
the Pathshala, the classes they teach for one of its teachers, and "not found"
for anybody else. Waiting requests are the administrators' only. Each report is
a couple of GROUP BYs over the whole Pathshala, never a query per child.

**The parent's phone on the Students tab is one they gave on the request.**
`RequestEnrolmentCommand` requires a contact name and phone, stored on the
enrolment. The Pathshala sees it whatever the parent's directory privacy
setting (confirmed with the user), which is exactly why it is asked for on a
form that says so rather than looked up in member-family-service - a lookup
would have let anybody holding `Pathshala.Manage` read the private phone of any
member id. Requests made before this carry none, and the tab says so.

**The donation ledger is record-keeping only.** Teachers do not see it - who
gave how much is the administrators' to keep. Amounts are integer paise.

**An announcement and a receipt are two steps (2026-10-24).** Members often
announce a gift and pay later (the user's requirement), so a donation is
recorded paid or `Pledged`. A pledge has no receipt number, mode or paid date;
`MarkDonationPaidCommand` gives it all three. A number on a slip therefore always
means money received - trust-service's rule. The ledger reports both totals:
received, and announced but not yet paid. Every donation recorded before this
was received, and the `DonationPledges` migration made each one Paid on the day
it was given; `DonationPledgesMigrationTests` runs it against a legacy row.

**Deleting a paid entry keeps its row; deleting an announcement erases it.**
The admin panel calls both Delete (the user asked for Edit and Delete rather
than Void). A receipt number handed to a donor has to keep pointing at
something, so a paid entry is voided: it leaves the totals and the ledger's
default view, keeps its required reason, and its number is never issued again.
An announcement has no receipt and no slip points at it, so
`DeleteDonationCommand` removes the row outright (the user's choice) and
`pathshala.donation.erased.v1` is the one record that it existed - ids, the
amount and who removed it, never the donor's name, which would otherwise outlive
the row in the append-only audit log. Neither command does the other's job: the
aggregate refuses to erase a receipt or void an announcement. Receipt numbers
are one more than the highest ever issued; the unique index on (Pathshala,
ReceiptNumber) is what holds when two are issued at once, and Postgres lets any
number of pledges share its null. Correcting an entry is `UpdateDonationCommand`,
which never turns a pledge into a payment - that issues a receipt, and is its
own act.

**Announcements keep their text here.** `SendAnnouncementCommand` stores the
title and body and raises ids only: `pathshala.announcement.sent.v1` (who sent
it, to how many) and one `pathshala.announcement.addressed.v1` per recipient,
which audit-notification-service turns into that parent's in-app notification -
one per event, because its (source message, channel) unique index allows no
more. The recipients are the parent who asked for each place with a place or a
request, plus the student once they have their own account. Parents read the
text through `ListMyAnnouncementsQuery`. There is no SMS or WhatsApp - the
platform has no provider for either.

## The register

**The unique index on `(EnrolmentId, ClassDate)` is what keeps a child's
attendance record true.** Every number this service reports — the percentage,
the present count, the progress screen — is a count over that table, so a
duplicate does not fail loudly; it quietly inflates one child's record with
nothing on any screen to notice it by. A teacher submitting from a phone
submits twice as a matter of course, and both submissions read no existing row
before either writes.

Three things follow.

**The register is one submission, not twenty-five.** One command, one answer.
Twenty-five separate calls from a Pathshala's wifi is how half a register ends
up recorded.

**Re-marking amends.** Correcting Present to Excused after a parent explains is
the ordinary case, not an error.

**And because it amends, it has to be readable — which for a long time it was
not.** Every mark not named in a submission is left exactly as it was, so a
teacher fixing one child had no way to see the other twenty-four. There was no
read for a class's register at all: the only attendance query was per enrolment,
which answers for one child and is gated on owning that enrolment.

That is a gap you could not see from either side on its own. The write path was
complete and correct; the read path for a *parent* was complete and correct; the
teacher's read simply did not exist, and nothing failed to point it out because
no screen had ever tried. `GetClassRegisterQuery` closes it, and the admin class
screen loads it before showing the form rather than after.

**"Before the class has met" is judged with a day's slack (2026-10-24).** A
register's `ClassDate` is the teacher's own calendar day, and this service's
clock is UTC - which in India is still yesterday until 5:30 in the morning, so
"after UTC today" refused a register for a class that had just met. A date up to
UTC tomorrow is accepted; anything later has not happened anywhere. The admin
class screen does the exact check on the browser's own date (no future dates in
the picker, and it opens on the last day the class meets rather than today), so
the slack is only ever reached by a teacher east of Greenwich.

A date nobody has marked answers with an empty list, not 404. "Not marked yet"
is a normal state of a register, and a teacher opening next Sunday's should get
a blank form rather than an error.

**A class's exams had the same shape of hole.** `ScheduleExamCommand` answered
with the new exam's id and nothing ever listed them again, so recording a result
meant still holding the response that created the exam. An exam set last week
could not be marked this week by any route the platform offered.
`ListClassExamsQuery` answers with the exams and the marks already in them
together — a teacher entering results needs to know who has one, because
re-recording amends silently.

It is gated on `Pathshala.Exams.Write` rather than on reading records, because
it answers for the whole class at once. A parent entitled to their own child's
marks reads them through the progress view, which is scoped to one enrolment.

**The whole register — read, amend and insert — happens on one connection of
the repository's own, outside the request's transaction.** Two mistakes were
made here, both worth keeping:

- *Splitting the read from the write.* Amending on the request's context and
  inserting on a separate one means the request's transaction holds row locks
  the second connection waits for. Two teachers submitting the same register
  deadlock each other.
- *Catching the wrong exception type.* EF surfaces a unique violation as a
  `DbUpdateException` directly, but classifies a **deadlock** as transient and
  rewraps it in an `InvalidOperationException`. A catch typed to
  `DbUpdateException` handles one and silently misses the other. Walk the inner
  chain for the SQLSTATE instead — see `EnrolmentRepository.IsContention`.

Marks are also inserted in a deterministic order, so concurrent copies of one
register take their locks in the same order and cannot form a cycle at all.

## Decisions worth knowing before you change this service

**A Pathshala's address became a structured pick on 2026-09-12, not a
free-text field.** `Pathshala.Address` was one nullable string; it is a
`PathshalaAddress` now (a free-text line plus `CountryId`/`StateId`/`CityId`/
`LocalityId`, all master-data-service ids), the same free-text-to-master-
data-pick move member-family-service already made for `MemberProfile`'s own
address. Nullable as a whole, unlike member-family-service's
`PermanentAddress`: a Pathshala can simply have no address recorded, where a
member profile always carries *an* address object even with every field
inside it empty. The cascade goes Country -> State -> City -> Locality
because master-data-service's own `Locality` is scoped to a `City`, which is
scoped to a `Country` and, optionally, a `State` — there is no way to filter
a Locality list without a City chosen first. The address is set on creation
and corrected with `UpdatePathshalaCommand`, which replaces the name, the
whole address and the contact person together - a partial patch would need
"leave it" and "clear it" told apart on every field, and the admin form
always has all three on screen anyway.

The migration renamed the old `address` column to `address_line` rather than
discarding it — a Pathshala's one address always meant its physical line —
and added the four new nullable id columns starting null, the same shape
member-family-service's own address migration used: there is no confident
lookup from typed free text to a specific master-data-service row, so
nothing already in that free text is back-filled into a City or Locality id.

**Enrolment is two steps, and the second one is not bureaucracy.** The endpoint
is `POST /pathshalas/{id}/enrollments`, not `/classes/{id}/...`, so somebody at
the Pathshala still has to pick the class — the parent does not know the
rosters. That step also answers a question this service structurally cannot: a
parent enrols a child by `ChildProfileId`, and whether that child is theirs is
member-family-service's fact. Asking synchronously would reach across a service
boundary this repo avoids; mirroring it would mean a projection that is stale
exactly when a family is new. The staff placing the child know the family and
were going to look anyway. An unplaced request grants access to nothing,
because nothing has been recorded against it.

**Enrolments, attendance and results are not in the Pathshala aggregate.** One
class of twenty-five generates roughly twelve hundred attendance rows a year; a
Pathshala with eight classes generates ten thousand. An aggregate that loaded
them would read the year to mark one register. Same decision, for the same
reason, as celebrity-voting-service made about votes.

**There is no `ProgressRecord` table.** DATA-MODEL.md §9 has one holding
`AttendancePct` and `AverageScore`; both are counts over tables this service
already owns, and a stored copy drifts — a corrected mark or an amended
register would leave the progress screen quietly wrong until something
recomputed it. Only `ParticipationNotes`, which nothing can derive, would have
justified the table, and no screen asks for it. `GetMyProgressQuery` computes.

**There is no `PathshalaEvent` either.** events-service exists and does this
properly, with capacity and a waitlist. A second, weaker event system inside
this service would be the thing a Samaaj had to discover it should not use.

**Exam averages are computed as percentages, not raw scores.** An exam out of
20 and one out of 100 are not comparable marks; averaging the raw numbers would
let the longer paper decide the result.

**"My Exams" has three states, not the wireframe's two.** An exam sat last week
and not yet marked is neither Upcoming nor Completed, and showing it as
Completed with a blank result reads as a zero. It is `AwaitingResult`.

**Excused days are not counted against a student.** The percentage is over
present and absent only, so a child excused for half a term does not appear to
have a poor record. When nothing counts yet the percentage is null rather than
zero — zero is a claim, and the wrong one.

**One current session per Pathshala.** It is what decides where a new enrolment
lands, and two answers to that is worse than none: a child enrolled into last
year would look enrolled and appear on no register. Opening a new session
stands the old one down and leaves its records alone.

**Withdrawing is not erasure.** A child who leaves in March still attended from
June, and a Pathshala asked what its attendance was that year has to be able to
answer.

**Class rosters carry member ids, not names.** A name here would be a copy of
member-family-service's data kept in step by nothing.

**`GetAttendanceSummaryQuery` is the one tally on this platform that is not
scoped to an enrolment, a class, or a Pathshala.** It answers the admin
dashboard's Reports & Analytics screen's "average attendance" card, and reuses
`AttendanceTally` verbatim rather than a new response shape - the same
`Present`/`Absent`/`Excused`/`Percentage` `GetMyAttendanceQuery` already
returns per student, computed here with the enrolment filter dropped instead
of added. It is gated on `Pathshala.Manage` alone, unlike `ListPathshalasQuery`'s
`Members.Read`: a tenant-wide figure is a management report, not something a
parent's own directory call should ever be able to answer.

## Dependencies

- PostgreSQL — `samaajconnect_pathshala`
- Kafka via the Outbox (`OutboxDispatcher`), publishing; and one consumer
  (`IntegrationEventConsumer`) on a single explicit topic
- No Redis

## Testing

- `Sangam.Pathshala.UnitTests` — the aggregates: one current session, timetable
  overlaps, teaching *this* class, the enrolment states, who an enrolment
  belongs to.
- `Sangam.Pathshala.IntegrationTests` — Testcontainers against a real Postgres.
  `PathshalaFlowTests` walks the whole thing end to end;
  `AttendanceIndexTests` reads `pg_indexes`, makes two colliding inserts
  directly, fires ten simultaneous copies of one register, and exercises the
  conversion link.
- `scripts/smoke-through-gateway.sh` runs the same flow through YARP, including
  granting the `PathshalaTeacher` role and signing in again — which is what
  proves the role actually carries the permission.

The conversion consumer is tested through the command it dispatches rather than
through a broker. The loop around it is Confluent's; the decision is ours.
