import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Enrolment,
  MyAttendance,
  MyClass,
  MyExam,
  MyProgress,
  Pathshala,
} from './pathshala.models';

/**
 * Every call this app makes to pathshala-service.
 *
 * Module-gated on `pathshala`, its own key.
 *
 * Running a Pathshala - opening a session, creating classes, assigning
 * teachers, marking the register, setting exams, placing a child in a class -
 * all needs `Pathshala.Manage`, which is a Samaaj admin's or a Pathshala
 * teacher's. None of it is here. What is here is what a parent may do: ask for
 * a place, and read their own child's records.
 */
@Injectable({ providedIn: 'root' })
export class PathshalaApi {
  private readonly http = inject(HttpClient);

  /** The Samaaj's Pathshalas, with the counts the directory card shows. */
  list(): Observable<Pathshala[]> {
    return this.http.get<Pathshala[]>('/v1/pathshala/pathshalas');
  }

  /** Every place this member has asked for, for a child or for themselves. */
  myEnrolments(): Observable<Enrolment[]> {
    return this.http.get<Enrolment[]>('/v1/pathshala/enrollments');
  }

  /**
   * Asks for a place. The Pathshala decides which class, which is why this
   * answers with a `Requested` enrolment rather than an enrolled one.
   */
  enrol(pathshalaId: string, childProfileId: string): Observable<Enrolment> {
    return this.http.post<Enrolment>(
      `/v1/pathshala/pathshalas/${pathshalaId}/enrollments`,
      { childProfileId },
    );
  }

  /**
   * The class a child was placed in.
   *
   * **Answers 409 `Enrolment.NotPlaced` while the child is still waiting**, so
   * only ask once the enrolment carries a `classId`. Asking speculatively would
   * put an expected error on every visit to a waiting enrolment.
   */
  myClass(enrolmentId: string): Observable<MyClass> {
    return this.http.get<MyClass>(`/v1/pathshala/enrollments/${enrolmentId}/my-class`);
  }

  myAttendance(enrolmentId: string): Observable<MyAttendance> {
    return this.http.get<MyAttendance>(`/v1/pathshala/enrollments/${enrolmentId}/attendance`);
  }

  /** Upcoming, awaiting result and completed in one list. Empty until placed. */
  myExams(enrolmentId: string): Observable<MyExam[]> {
    return this.http.get<MyExam[]>(`/v1/pathshala/enrollments/${enrolmentId}/exams`);
  }

  myProgress(enrolmentId: string): Observable<MyProgress> {
    return this.http.get<MyProgress>(`/v1/pathshala/enrollments/${enrolmentId}/progress`);
  }
}
