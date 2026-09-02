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
| `TeacherAssignment` | Who teaches a class. Unique per (class, teacher) |
| `StudentEnrolment` | Aggregate root. One child's place: requested, then placed |
| `AttendanceEntry` | One mark. Written directly, unique per (enrolment, date) |
| `Exam` | Aggregate root. Set for a class |
| `ExamResult` | One mark. Written directly, unique per (exam, enrolment) |

Two entities in DATA-MODEL.md §9 are deliberately absent. See "Decisions".

## Commands

| Command | Policy | Notes |
|---|---|---|
| `CreatePathshalaCommand` | `SuperAdmin` + `Pathshala.Manage` | The one act reserved to the platform |
| `OpenSessionCommand` | `Pathshala.Manage` | Makes the new session current |
| `CreateClassCommand` | `Pathshala.Manage` | |
| `AddClassSlotCommand` | `Pathshala.Manage` | Refuses an overlap |
| `AssignTeacherCommand` | `Pathshala.Manage` | Assign and remove in one command |
| `DeactivatePathshalaCommand` | `Pathshala.Manage` | Records are kept; enrolments stop |
| `RequestEnrolmentCommand` | `Members.Read` | A parent asks |
| `PlaceStudentCommand` | `Pathshala.Manage` | The Pathshala decides, and picks the class |
| `WithdrawStudentCommand` | `Pathshala.Manage` | Off the roll; attendance and results stay |
| `MarkAttendanceCommand` | `Pathshala.Attendance.Write` **and** teaching this class | The whole register in one submission |
| `ScheduleExamCommand` | `Pathshala.Exams.Write` **and** teaching this class | |
| `RecordExamResultCommand` | `Pathshala.Exams.Write` **and** teaching this class | Re-recording amends |
| `ConsumeIntegrationEventCommand` | `[InternalRequest]` | Links a converted child's enrolments to their new account |

## Queries

| Query | Policy | Notes |
|---|---|---|
| `ListPathshalasQuery` | `Members.Read` | Counts, not rosters |
| `GetPathshalaQuery` | `Members.Read` | Sessions and classes |
| `ListEnrolmentRequestsQuery` | `Pathshala.Manage` | The placement queue |
| `GetClassRollQuery` | `Members.Read` + teaching this class | A list of somebody's children |
| `GetClassRegisterQuery` | `Members.Read` + teaching this class | One date's marks, so amending is not a guess |
| `ListClassExamsQuery` | `Pathshala.Exams.Write` + teaching this class | The class's exams with their marks |
| `ListMyEnrolmentsQuery` | `Members.Read` | What this member asked for, or holds |
| `GetMyClassQuery` | `Members.Read` + owns this enrolment | |
| `GetMyAttendanceQuery` | `Members.Read` + owns this enrolment | |
| `GetMyExamsQuery` | `Members.Read` + owns this enrolment | |
| `GetMyProgressQuery` | `Members.Read` + owns this enrolment | Computed, not stored |

## Events published

- `pathshala.created.v1`
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
