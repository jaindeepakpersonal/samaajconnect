import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ActivationCode,
  AttendanceStatus,
  AdminUser,
  AssignRoleResult,
  AuditLogEntry,
  ClassExam,
  ModerationDecision,
  ModerationQueueEntry,
  Enrolment,
  Pathshala,
  PathshalaClass,
  PathshalaDetail,
  Broadcast,
  BroadcastResult,
  ConversionRequest,
  CreateTenantRequest,
  InviteAdminRequest,
  InviteAdminResult,
  ModuleDescriptor,
  PendingActivation,
  RegisterEntry,
  RoleMatrix,
  Tenant,
  TenantStatus,
} from './admin.models';

/**
 * Every call the admin panel makes, in one place.
 *
 * Paths are relative; the shared tenant interceptor rewrites them to the
 * gateway and `adminScopeInterceptor` adds the Super Admin override when a
 * Samaaj is selected (root `CLAUDE.md` §7). Nothing here decides what an admin
 * may do - the screens gate on roles for the sake of the UI, and the service
 * behind each of these re-checks properly.
 */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  // ---- Samaaj -----------------------------------------------------------

  listTenants(filter?: { status?: TenantStatus | null; search?: string | null }): Observable<Tenant[]> {
    let params = new HttpParams();

    if (filter?.status) {
      params = params.set('status', filter.status);
    }

    if (filter?.search) {
      params = params.set('search', filter.search);
    }

    return this.http.get<Tenant[]>('/v1/identity/tenants', { params });
  }

  createTenant(request: CreateTenantRequest): Observable<Tenant> {
    return this.http.post<Tenant>('/v1/identity/tenants', request);
  }

  /**
   * Deactivating and archiving re-ask for the caller’s own password; activating
   * does not. The server decides which, so `password` is always sent and is
   * simply ignored where it is not needed.
   */
  changeTenantStatus(
    id: string,
    status: TenantStatus,
    password?: string,
  ): Observable<Tenant> {
    return this.http.patch<Tenant>(`/v1/identity/tenants/${id}/status`, { status, password });
  }

  /** Replaces the whole set. The screen is a row of toggles saved together. */
  setTenantModules(id: string, enabledModules: readonly string[]): Observable<Tenant> {
    return this.http.put<Tenant>(`/v1/identity/tenants/${id}/modules`, { enabledModules });
  }

  /** The closed list of module keys, with the labels the panel shows. */
  listModules(): Observable<ModuleDescriptor[]> {
    return this.http.get<ModuleDescriptor[]>('/v1/identity/tenants/modules');
  }

  setGrievanceContact(
    id: string,
    contact: { name: string | null; email: string | null; phone: string | null },
  ): Observable<Tenant> {
    return this.http.put<Tenant>(`/v1/identity/tenants/${id}/grievance-contact`, contact);
  }

  // ---- Administrators and roles -----------------------------------------

  roleMatrix(): Observable<RoleMatrix> {
    return this.http.get<RoleMatrix>('/v1/identity/roles');
  }

  /**
   * Grants or revokes one permission on one role, for the Samaaj in scope.
   *
   * Answers with the whole matrix rather than the one cell, so the screen never
   * has to guess what the change did to the rest of it — and so a refusal
   * leaves the screen showing what is actually true rather than the optimistic
   * tick it drew a moment ago.
   */
  setRolePermission(roleId: string, permission: string, granted: boolean) {
    const path = `/v1/identity/roles/${roleId}/permissions/${encodeURIComponent(permission)}`;

    return this.http.put<RoleMatrix>(path, { granted });
  }

  listAdmins(): Observable<AdminUser[]> {
    return this.http.get<AdminUser[]>('/v1/identity/admins');
  }

  inviteAdmin(request: InviteAdminRequest): Observable<InviteAdminResult> {
    return this.http.post<InviteAdminResult>('/v1/identity/admins', request);
  }

  /** One role per call: the screen is a checkbox, so this is one checkbox. */
  setRole(userId: string, role: string, granted: boolean): Observable<AssignRoleResult> {
    return this.http.put<AssignRoleResult>(
      `/v1/identity/admins/${userId}/roles/${encodeURIComponent(role)}`,
      { granted },
    );
  }

  listPendingActivations(): Observable<PendingActivation[]> {
    return this.http.get<PendingActivation[]>('/v1/identity/activations/pending');
  }

  /** The plaintext code, returned once. Only its hash is stored. */
  issueActivationCode(userId: string): Observable<ActivationCode> {
    return this.http.post<ActivationCode>(`/v1/identity/activations/${userId}/code`, {});
  }

  // ---- Community ---------------------------------------------------------

  listConversionRequests(): Observable<ConversionRequest[]> {
    return this.http.get<ConversionRequest[]>('/v1/children/conversion-requests');
  }

  decideConversion(
    requestId: string,
    approve: boolean,
    note: string | null,
  ): Observable<ConversionRequest> {
    return this.http.post<ConversionRequest>(
      `/v1/children/conversion-requests/${requestId}/decide`,
      { approve, note },
    );
  }

  listAuditLogs(filter?: { action?: string | null; entityName?: string | null }): Observable<AuditLogEntry[]> {
    let params = new HttpParams().set('limit', 100);

    if (filter?.action) {
      params = params.set('action', filter.action);
    }

    if (filter?.entityName) {
      params = params.set('entityName', filter.entityName);
    }

    return this.http.get<AuditLogEntry[]>('/v1/audit/logs', { params });
  }

  // ---- Notifications ----------------------------------------------------

  /**
   * Announces something to every member of the Samaaj currently in scope.
   *
   * The Samaaj is never in the body. A Samaaj Admin's token names theirs, and a
   * Super Admin's scope selection travels as the override header
   * `adminScopeInterceptor` adds - which the gateway audits. A tenant id in the
   * payload would be a second way to say it, and the one nothing checks.
   */
  broadcast(title: string, body: string): Observable<BroadcastResult> {
    return this.http.post<BroadcastResult>('/v1/notifications/broadcast', { title, body });
  }

  listBroadcasts(): Observable<Broadcast[]> {
    return this.http.get<Broadcast[]>('/v1/notifications/broadcasts');
  }

  // ---- Timeline moderation ----------------------------------------------

  /**
   * Posts awaiting review, plus approved posts members have reported.
   *
   * Reported posts are in the same queue deliberately: a separate reports
   * screen is a screen somebody has to remember to open, and the point of a
   * report is that it should not wait for that.
   *
   * Module-gated on `community` at the gateway, so a Samaaj that has switched
   * that module off answers 404 here rather than an empty queue.
   */
  moderationQueue(): Observable<ModerationQueueEntry[]> {
    return this.http.get<ModerationQueueEntry[]>('/v1/timeline/posts/moderation-queue');
  }

  /**
   * Records a decision. The service requires `reason` for Reject and Hide —
   * those are the cases where the member will ask why — and ignores it
   * otherwise.
   */
  moderatePost(
    postId: string,
    decision: ModerationDecision,
    reason: string | null,
  ): Observable<unknown> {
    return this.http.post(`/v1/timeline/posts/${postId}/moderate`, { decision, reason });
  }

  /**
   * The Samaaj directory, used only to put a name against a post's author id.
   *
   * One call for the whole queue rather than one per row, the same approach the
   * member portal takes. An administrator's directory includes members who have
   * taken themselves out of it, so a moderator never meets an unresolvable id
   * because the author chose to be unlisted.
   */
  listMembers(): Observable<{ id: string; fullName: string }[]> {
    return this.http.get<{ id: string; fullName: string }[]>('/v1/members?limit=100');
  }

  // ---- Pathshala ---------------------------------------------------------

  listPathshalas(): Observable<Pathshala[]> {
    return this.http.get<Pathshala[]>('/v1/pathshala/pathshalas');
  }

  pathshala(id: string): Observable<PathshalaDetail> {
    return this.http.get<PathshalaDetail>(`/v1/pathshala/pathshalas/${id}`);
  }

  /**
   * Opens an academic session, which becomes the current one.
   *
   * Answers with the whole Pathshala rather than the session, which is why the
   * screen never has to guess what opening one did to the session that was
   * current a moment ago.
   */
  openSession(
    pathshalaId: string,
    label: string,
    startDate: string,
    endDate: string,
  ): Observable<PathshalaDetail> {
    return this.http.post<PathshalaDetail>(`/v1/pathshala/pathshalas/${pathshalaId}/sessions`, {
      label,
      startDate,
      endDate,
    });
  }

  createClass(
    pathshalaId: string,
    sessionId: string,
    name: string,
    roomLabel: string | null,
  ): Observable<PathshalaClass> {
    return this.http.post<PathshalaClass>(`/v1/pathshala/pathshalas/${pathshalaId}/classes`, {
      sessionId,
      name,
      roomLabel,
    });
  }

  /** Children a parent has asked to enrol, waiting for somebody to place them. */
  enrolmentRequests(pathshalaId: string): Observable<Enrolment[]> {
    return this.http.get<Enrolment[]>(
      `/v1/pathshala/pathshalas/${pathshalaId}/enrollments/requests`,
    );
  }

  /**
   * Places a requested child in a class, or turns the request down.
   *
   * `place: false` needs no class, and the service refuses a `place: true` with
   * no class — a placement into nothing is not a placement.
   */
  placeStudent(
    enrolmentId: string,
    classId: string | null,
    place: boolean,
  ): Observable<Enrolment> {
    return this.http.post<Enrolment>(`/v1/pathshala/enrollments/${enrolmentId}/placement`, {
      classId,
      place,
    });
  }

  /** Who is on a class's roll. Teachers of that class, and administrators. */
  classRoll(classId: string): Observable<Enrolment[]> {
    return this.http.get<Enrolment[]>(`/v1/pathshala/classes/${classId}/roll`);
  }

  /** Assigns a teacher to a class, or takes one off it. */
  assignTeacher(
    classId: string,
    teacherMemberId: string,
    assign: boolean,
  ): Observable<PathshalaClass> {
    return this.http.post<PathshalaClass>(`/v1/pathshala/classes/${classId}/teachers`, {
      teacherMemberId,
      assign,
    });
  }

  /** Adds a weekly slot. The service refuses one that overlaps another that day. */
  addClassSlot(
    classId: string,
    dayOfWeek: string,
    startTime: string,
    endTime: string,
  ): Observable<PathshalaClass> {
    return this.http.post<PathshalaClass>(`/v1/pathshala/classes/${classId}/schedule`, {
      dayOfWeek,
      startTime,
      endTime,
    });
  }

  /**
   * The register as it currently stands for one date.
   *
   * Read before the form is shown, never after: submitting amends what is there
   * and leaves anything not re-sent as it was, so a form that started blank
   * would quietly turn a correction into a half-recorded register.
   */
  classRegister(classId: string, date: string): Observable<RegisterEntry[]> {
    return this.http.get<RegisterEntry[]>(
      `/v1/pathshala/classes/${classId}/register?date=${date}`,
    );
  }

  /** The whole register in one submission — one command, one transaction, one answer. */
  markAttendance(
    classId: string,
    classDate: string,
    marks: readonly { enrolmentId: string; status: AttendanceStatus }[],
  ): Observable<unknown> {
    return this.http.post(`/v1/pathshala/classes/${classId}/attendance`, { classDate, marks });
  }

  /** This class's exams, each with the marks already recorded in it. */
  classExams(classId: string): Observable<ClassExam[]> {
    return this.http.get<ClassExam[]>(`/v1/pathshala/classes/${classId}/exams`);
  }

  scheduleExam(
    classId: string,
    title: string,
    examDate: string,
    maxScore: number,
  ): Observable<unknown> {
    return this.http.post(`/v1/pathshala/classes/${classId}/exams`, {
      title,
      examDate,
      maxScore,
    });
  }

  /** Records a mark, or amends one already recorded. */
  recordExamResult(
    examId: string,
    enrolmentId: string,
    score: number,
    grade: string | null,
  ): Observable<unknown> {
    return this.http.post(`/v1/pathshala/exams/${examId}/results`, { enrolmentId, score, grade });
  }

  /** Takes a student off the roll. Their attendance and results are kept. */
  withdrawStudent(enrolmentId: string): Observable<Enrolment> {
    return this.http.delete<Enrolment>(`/v1/pathshala/enrollments/${enrolmentId}`);
  }

  /**
   * Names for children this panel already holds the ids of.
   *
   * Names only, and by id: pathshala-service stores a child by id and nothing
   * else, and the full child record carries a date of birth, a gender and the
   * parental-consent record that a queue printing a name has no business
   * receiving.
   */
  childNames(ids: readonly string[]): Observable<{ id: string; fullName: string }[]> {
    return this.http.get<{ id: string; fullName: string }[]>(
      `/v1/children/names?ids=${ids.join(',')}`,
    );
  }
}
