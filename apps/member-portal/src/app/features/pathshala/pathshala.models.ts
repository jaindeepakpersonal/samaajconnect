/**
 * Wire shapes for pathshala-service, mirroring `PathshalaResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with, read
 * from `StudentEnrolment.cs` and `AttendanceEntry.cs` rather than guessed - the
 * portal has shipped three bugs of exactly that shape (see this app's
 * `CLAUDE.md`).
 */

/** `EnrolmentStatus` in the domain. */
export type EnrolmentStatus = 'Requested' | 'Active' | 'Withdrawn' | 'Declined';

/** `AttendanceStatus` in the domain. */
export type AttendanceStatus = 'Present' | 'Absent' | 'Excused';

/**
 * The exam states the service reports.
 *
 * Three, not the wireframe's two. An exam sat last week and not yet marked is
 * neither Upcoming nor Completed, and showing it as Completed with an empty
 * result column reads as a zero.
 */
export type ExamStatus = 'Upcoming' | 'AwaitingResult' | 'Completed';

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

  /** False when there is no open session, so the enrol button has nowhere to go. */
  readonly acceptsEnrolments: boolean;
}

/**
 * One child's place.
 *
 * `classId` and `className` are null while the request is still waiting to be
 * placed. That is the distinction a parent most needs: waiting is not refused,
 * and it is not enrolled either.
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

/** The wireframe's "My Class" card. Teachers are ids; the count is what ships. */
export interface MyClass {
  readonly enrolmentId: string;
  readonly pathshalaId: string;
  readonly pathshalaName: string;
  readonly classId: string;
  readonly className: string;
  readonly roomLabel: string | null;
  readonly sessionLabel: string;
  readonly schedule: readonly ScheduleSlot[];
  readonly teacherMemberIds: readonly string[];

  /** A count, not a list of other people's children. */
  readonly classmateCount: number;
}

export interface ScheduleSlot {
  readonly dayOfWeek: string;
  readonly startTime: string;
  readonly endTime: string;
}

/**
 * The wireframe's three attendance tiles, and the days behind them.
 *
 * `percentage` is null when nothing has been marked yet - **not zero**. A child
 * enrolled last week who has not had a class has no attendance record, and
 * printing 0% would tell their parent they had missed everything.
 */
export interface MyAttendance {
  readonly enrolmentId: string;
  readonly percentage: number | null;
  readonly present: number;
  readonly absent: number;
  readonly excused: number;
  readonly days: readonly AttendanceDay[];
}

export interface AttendanceDay {
  readonly classDate: string;
  readonly status: AttendanceStatus;
}

/**
 * One row of the wireframe's exam table.
 *
 * `score` is null both for an exam not yet sat and for one sat but not marked;
 * `status` is the only thing that tells those apart.
 */
export interface MyExam {
  readonly examId: string;
  readonly title: string;
  readonly examDate: string;
  readonly maxScore: number;
  readonly status: ExamStatus;
  readonly score: number | null;
  readonly grade: string | null;
}

/**
 * The wireframe's "My Progress".
 *
 * Both averages are null rather than zero when there is nothing to average, for
 * the same reason the attendance percentage is.
 */
export interface MyProgress {
  readonly enrolmentId: string;
  readonly sessionLabel: string | null;
  readonly attendancePercentage: number | null;
  readonly present: number;
  readonly absent: number;
  readonly excused: number;
  readonly examsSat: number;
  readonly averageScorePercentage: number | null;
}

/** What each enrolment state is called on screen, rather than its enum name. */
export const EnrolmentStatusLabels: Readonly<Record<EnrolmentStatus, string>> = {
  Requested: 'Waiting for a place',
  Active: 'Enrolled',
  Withdrawn: 'Withdrawn',
  Declined: 'Not offered a place',
};

/** What each exam state is called on screen. */
export const ExamStatusLabels: Readonly<Record<ExamStatus, string>> = {
  Upcoming: 'Upcoming',
  AwaitingResult: 'Awaiting result',
  Completed: 'Completed',
};
