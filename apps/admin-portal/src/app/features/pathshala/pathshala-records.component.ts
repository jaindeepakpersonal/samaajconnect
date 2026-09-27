import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { ClassAttendance, ExamOverview, ProgressRow } from '../../core/admin.models';

export type RecordsKind = 'exams' | 'progress' | 'attendance';

/**
 * The three read-only tabs: every exam across the Pathshala, each child's
 * progress, and each class's attendance. Recording any of them happens on a
 * class's own page, where the register and the mark sheets are; these are
 * the view from above.
 *
 * **A missing number is shown as a dash, never a zero.** A class with no
 * register marked has no attendance percentage, and a child who sat no exam
 * has no average - the service sends null for both, and "0%" would be a claim
 * about a child nobody has said anything about.
 */
@Component({
  selector: 'app-pathshala-records',
  imports: [DatePipe, RouterLink],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    <div class="card">
      @switch (kind()) {
        @case ('exams') {
          <h2>Exams</h2>
          <p class="small">Exams are set, and marks recorded, on each class's own page.</p>

          @if (loading()) {
            <p class="empty" role="status">Loading…</p>
          } @else if (error()) {
            <p class="empty">Nothing to show until it can be read.</p>
          } @else if (exams().length === 0) {
            <p class="empty">No exams set yet.</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Exams across this Pathshala</caption>
                <thead>
                  <tr><th>Exam</th><th>Class</th><th>Date</th><th>Out of</th><th>Marked</th><th>Average</th></tr>
                </thead>
                <tbody>
                  @for (exam of exams(); track exam.examId) {
                    <tr>
                      <td><b>{{ exam.title }}</b></td>
                      <td>
                        <a class="btn link" [routerLink]="['/pathshala', pathshalaId(), 'classes', exam.classId]">
                          {{ exam.className }}
                        </a>
                      </td>
                      <td>{{ exam.examDate | date: 'd MMM y' }}</td>
                      <td>{{ exam.maxScore }}</td>
                      <td>{{ exam.resultsRecorded }} of {{ exam.studentCount }}</td>
                      <td>{{ percent(exam.averageScorePercentage) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }

        @case ('progress') {
          <h2>Progress report</h2>
          <p class="small">
            Each enrolled child's attendance and exam average this far. Excused absences do not
            count against a child.
          </p>

          @if (loading()) {
            <p class="empty" role="status">Loading…</p>
          } @else if (error()) {
            <p class="empty">Nothing to show until it can be read.</p>
          } @else if (progress().length === 0) {
            <p class="empty">Nobody is enrolled yet.</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Progress of each child</caption>
                <thead>
                  <tr>
                    <th>Child</th><th>Class</th><th>Present</th><th>Absent</th><th>Excused</th>
                    <th>Attendance</th><th>Exams sat</th><th>Average</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of progress(); track row.enrolmentId) {
                    <tr>
                      <td><b>{{ childName(row.childProfileId) }}</b></td>
                      <td>{{ row.className ?? '—' }}</td>
                      <td>{{ row.present }}</td>
                      <td>{{ row.absent }}</td>
                      <td>{{ row.excused }}</td>
                      <td>{{ percent(row.attendancePercentage) }}</td>
                      <td>{{ row.examsSat }}</td>
                      <td>{{ percent(row.averageScorePercentage) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }

        @case ('attendance') {
          <h2>Attendance</h2>
          <p class="small">Registers are marked on each class's own page.</p>

          @if (loading()) {
            <p class="empty" role="status">Loading…</p>
          } @else if (error()) {
            <p class="empty">Nothing to show until it can be read.</p>
          } @else if (attendance().length === 0) {
            <p class="empty">No classes yet.</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Attendance by class</caption>
                <thead>
                  <tr>
                    <th>Class</th><th>Session</th><th>Students</th><th>Days marked</th>
                    <th>Present</th><th>Absent</th><th>Excused</th><th>Attendance</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of attendance(); track row.classId) {
                    <tr>
                      <td>
                        <a class="btn link" [routerLink]="['/pathshala', pathshalaId(), 'classes', row.classId]">
                          <b>{{ row.className }}</b>
                        </a>
                      </td>
                      <td>{{ row.sessionLabel }}</td>
                      <td>{{ row.studentCount }}</td>
                      <td>{{ row.daysMarked }}</td>
                      <td>{{ row.present }}</td>
                      <td>{{ row.absent }}</td>
                      <td>{{ row.excused }}</td>
                      <td>{{ percent(row.percentage) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      }
    </div>
  `,
})
export class PathshalaRecordsComponent implements OnInit {
  private readonly api = inject(AdminApi);
  readonly pathshalaId = input.required<string>();
  readonly kind = input.required<RecordsKind>();

  readonly exams = signal<readonly ExamOverview[]>([]);
  readonly progress = signal<readonly ProgressRow[]>([]);
  readonly attendance = signal<readonly ClassAttendance[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  ngOnInit(): void {
    const id = this.pathshalaId();
    const fail = (failure: unknown) => {
      this.error.set(describeError(failure));
      this.loading.set(false);
    };

    switch (this.kind()) {
      case 'exams':
        this.api.pathshalaExams(id).subscribe({
          next: (found) => this.loaded(() => this.exams.set(found)),
          error: fail,
        });
        break;
      case 'progress':
        this.api.progressReport(id).subscribe({
          next: (found) => {
            this.loaded(() => this.progress.set(found));
            this.loadNames(found.map((r) => r.childProfileId));
          },
          error: fail,
        });
        break;
      case 'attendance':
        this.api.attendanceOverview(id).subscribe({
          next: (found) => this.loaded(() => this.attendance.set(found)),
          error: fail,
        });
        break;
    }
  }

  childName(childProfileId: string): string {
    return this.names().get(childProfileId) ?? 'A child';
  }

  /** Null is "nothing to count yet", and reads as a dash. */
  percent(value: number | null): string {
    return value === null ? '—' : `${Math.round(value * 10) / 10}%`;
  }

  private loaded(apply: () => void): void {
    apply();
    this.loading.set(false);
  }

  private loadNames(ids: readonly string[]): void {
    const distinct = [...new Set(ids)];

    if (distinct.length === 0) {
      return;
    }

    this.api.childNames(distinct).subscribe({
      next: (found) => this.names.set(new Map(found.map((c) => [c.id, c.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }
}
