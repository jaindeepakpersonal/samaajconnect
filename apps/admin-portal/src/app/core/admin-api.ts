import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ActivationCode,
  Attendee,
  AttendanceStatus,
  AdminMember,
  AdminUser,
  AssignRoleResult,
  AuditLogEntry,
  Boli,
  BoliType,
  Campaign,
  CampaignDetail,
  CampaignResult,
  CampaignStatus,
  ClassExam,
  ModerationDecision,
  ModerationQueueEntry,
  Enrolment,
  GroupStatus,
  Pathshala,
  PathshalaClass,
  PathshalaDetail,
  Broadcast,
  BroadcastResult,
  ConversionRequest,
  CreateTenantRequest,
  InviteAdminRequest,
  InviteAdminResult,
  IssueStatus,
  MemberCorrection,
  ModuleDescriptor,
  OrganizerGroup,
  OrganizerType,
  Occasion,
  OccasionDetail,
  OccasionStatus,
  PendingActivation,
  PendingResult,
  RegisterEntry,
  ResultsVisibility,
  RoleMatrix,
  SamaajEvent,
  SocialIssue,
  Tenant,
  TenantStatus,
  VolunteerGroup,
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
   * Uploads a Samaaj's logo.
   *
   * Multipart, with no Content-Type set by hand: the browser writes the
   * boundary, and one set here would have none and produce a body no server can
   * parse. The part's declared type is sent and ignored — the service reads the
   * format out of the bytes.
   */
  uploadTenantLogo(tenantId: string, file: File): Observable<void> {
    const body = new FormData();
    body.append('file', file);

    return this.http.post<void>(`/v1/identity/tenants/${tenantId}/logo`, body);
  }

  /** Takes a logo down. Doing it twice is success. */
  removeTenantLogo(tenantId: string): Observable<void> {
    return this.http.delete<void>(`/v1/identity/tenants/${tenantId}/logo`);
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

  // ---- Member administration ---------------------------------------------

  /**
   * The Samaaj's directory, for an administrator.
   *
   * The same endpoint the member portal's directory calls, and it answers
   * differently: an administrator sees members who have taken themselves out of
   * the directory, and sees every field regardless of the level the member put
   * on it. Both follow from the same decision, that correcting somebody's
   * details is administrative work.
   */
  searchMembers(term: string | null, locality: string | null): Observable<AdminMember[]> {
    let params = new HttpParams().set('limit', 100);

    if (term && term.trim() !== '') {
      params = params.set('term', term.trim());
    }

    if (locality && locality.trim() !== '') {
      params = params.set('locality', locality.trim());
    }

    return this.http.get<AdminMember[]>('/v1/members', { params });
  }

  member(id: string): Observable<AdminMember> {
    return this.http.get<AdminMember>(`/v1/members/${id}`);
  }

  /**
   * Corrects somebody's details.
   *
   * A different path from the member portal's `PATCH /v1/members/{id}`, and the
   * difference is what the request does *not* carry. The whole-profile update
   * requires the member's privacy levels and whether they are listed — neither
   * of which any read available to an administrator returns — so correcting a
   * misspelt name through it meant guessing, and an unreadable level parses as
   * Private. This body has no such field to get wrong.
   */
  correctMember(id: string, correction: MemberCorrection): Observable<AdminMember> {
    return this.http.patch<AdminMember>(`/v1/members/${id}/details`, correction);
  }

  // ---- Pathshala ---------------------------------------------------------

  listPathshalas(): Observable<Pathshala[]> {
    return this.http.get<Pathshala[]>('/v1/pathshala/pathshalas');
  }

  pathshala(id: string): Observable<PathshalaDetail> {
    return this.http.get<PathshalaDetail>(`/v1/pathshala/pathshalas/${id}`);
  }

  /**
   * Creates the master record. **Super Admin only** (`DATA-MODEL.md` §9): the
   * record belongs to the platform operator, and everything about running it
   * belongs to the Samaaj.
   */
  createPathshala(
    name: string,
    address: string | null,
    contactPerson: string | null,
  ): Observable<Pathshala> {
    return this.http.post<Pathshala>('/v1/pathshala/pathshalas', {
      name,
      address,
      contactPerson,
    });
  }

  /**
   * Stops a Pathshala operating. Its records are kept and enrolments stop.
   *
   * Not deletion, and not reversible through this API — a Pathshala that closed
   * still taught the children who attended it, and a Samaaj asked what its
   * attendance was that year has to be able to answer.
   */
  deactivatePathshala(id: string): Observable<Pathshala> {
    return this.http.delete<Pathshala>(`/v1/pathshala/pathshalas/${id}`);
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

  // ---- Volunteer groups --------------------------------------------------

  listGroups(): Observable<VolunteerGroup[]> {
    return this.http.get<VolunteerGroup[]>('/v1/volunteer-groups/groups');
  }

  /**
   * Creates a group and installs its president in one act.
   *
   * The president is not optional: a group with nobody able to decide its
   * applications is a group whose join requests go nowhere, which is the shape
   * of gap this whole run of work has been closing.
   */
  createGroup(
    name: string,
    description: string | null,
    focusArea: string | null,
    presidentMemberId: string,
  ): Observable<VolunteerGroup> {
    return this.http.post<VolunteerGroup>('/v1/volunteer-groups/groups', {
      name,
      description,
      focusArea,
      presidentMemberId,
    });
  }

  /**
   * Stands a group down, or brings it back.
   *
   * Inactive is not deletion: the group keeps its members and its history and
   * simply takes no new applications. A Samaaj that ran a seva group for one
   * monsoon should still be able to see who was in it.
   */
  setGroupStatus(id: string, status: GroupStatus): Observable<VolunteerGroup> {
    return this.http.patch<VolunteerGroup>(
      `/v1/volunteer-groups/groups/${id}/status`,
      { status },
    );
  }

  /**
   * Hands the group to a different president.
   *
   * A Samaaj admin's decision, not the outgoing president's — the same split
   * as standing a group down. The service adds the new president to the group
   * if they were not already in it, and keeps the outgoing one on as an
   * ordinary member rather than losing the group its most experienced
   * volunteer as a side effect.
   */
  changeGroupPresident(id: string, newPresidentMemberId: string): Observable<VolunteerGroup> {
    return this.http.patch<VolunteerGroup>(
      `/v1/volunteer-groups/groups/${id}/president`,
      { newPresidentMemberId },
    );
  }

  // ---- Social issues -----------------------------------------------------

  /** What a reviewer has to decide about, oldest first. */
  issueApprovalQueue(): Observable<SocialIssue[]> {
    return this.http.get<SocialIssue[]>('/v1/social-issues/approval-queue');
  }

  /**
   * Moves an issue along its workflow.
   *
   * The target comes from the issue's own `availableTransitions` rather than
   * from anything the panel decides. `reason` is required on the refusing moves
   * and ignored on the rest — the service decides which, so it is always sent.
   */
  moveIssue(id: string, status: IssueStatus, reason: string | null): Observable<SocialIssue> {
    return this.http.post<SocialIssue>(`/v1/social-issues/${id}/status`, { status, reason });
  }

  // ---- Celebrity voting --------------------------------------------------

  listCampaigns(): Observable<Campaign[]> {
    return this.http.get<Campaign[]>('/v1/celebrity-voting/campaigns');
  }

  /** The ballot, and the tally when this caller may see it. */
  campaign(id: string): Observable<CampaignDetail> {
    return this.http.get<CampaignDetail>(`/v1/celebrity-voting/campaigns/${id}`);
  }

  /**
   * Creates a campaign. It starts as a draft.
   *
   * The service refuses a voting window that starts before nominations close,
   * so that members who vote early see the same ballot as members who vote
   * late.
   */
  createCampaign(campaign: {
    title: string;
    description: string | null;
    nominationStartAt: string;
    nominationEndAt: string;
    votingStartAt: string;
    votingEndAt: string;
    topN: number;
    resultsVisibility: ResultsVisibility;
  }): Observable<Campaign> {
    return this.http.post<Campaign>('/v1/celebrity-voting/campaigns', campaign);
  }

  /**
   * Moves a campaign on one stage.
   *
   * Draft → NominationsOpen → VotingOpen → Closed, and never backwards. The
   * service refuses `VotingOpen` on an empty ballot. Publishing is its own
   * call, not a status move.
   */
  moveCampaign(id: string, status: CampaignStatus): Observable<Campaign> {
    return this.http.post<Campaign>(`/v1/celebrity-voting/campaigns/${id}/status`, { status });
  }

  /**
   * Approves a nomination onto the ballot, or removes it.
   *
   * `approve` has no default in the request on purpose: a decision endpoint
   * whose safest value is implicit is one where a mistyped request quietly puts
   * somebody on a ballot. A candidate cannot be removed once voting has opened,
   * because removing them would discard the votes already cast for them.
   */
  decideCandidate(
    campaignId: string,
    candidateId: string,
    approve: boolean,
  ): Observable<unknown> {
    return this.http.post(
      `/v1/celebrity-voting/campaigns/${campaignId}/candidates/${candidateId}/decide`,
      { approve },
    );
  }

  /**
   * Computes the ranking and freezes it. Only from `Closed`, and only once.
   *
   * Publishing twice is refused rather than idempotent — unlike a Boli result,
   * where a repeat announcement changes nothing. Here a second publish would
   * compute a second ranking, and two rankings leave "the result" with no
   * referent.
   */
  publishCampaignResults(id: string): Observable<CampaignResult> {
    return this.http.post<CampaignResult>(
      `/v1/celebrity-voting/campaigns/${id}/results`,
      {},
    );
  }

  /** The frozen ranking. 404 until it has been published. */
  campaignResults(id: string): Observable<CampaignResult> {
    return this.http.get<CampaignResult>(`/v1/celebrity-voting/campaigns/${id}/results`);
  }

  // ---- Events ------------------------------------------------------------

  /**
   * This Samaaj's events.
   *
   * `includeDrafts` is honoured for a caller holding `Events.Publish` and
   * quietly ignored for anyone else — the service answers with the published
   * list rather than a 403, because refusing would tell a member that drafts
   * exist at all.
   */
  listEvents(includeDrafts: boolean, includePast: boolean): Observable<SamaajEvent[]> {
    const params = new HttpParams()
      .set('includeDrafts', includeDrafts)
      .set('includePast', includePast);

    return this.http.get<SamaajEvent[]>('/v1/events', { params });
  }

  /**
   * One event.
   *
   * A draft is visible to whoever holds `Events.Publish` and answers 404 to
   * everyone else — a member reaching one has guessed its id, and confirming it
   * exists is the leak.
   */
  event(id: string): Observable<SamaajEvent> {
    return this.http.get<SamaajEvent>(`/v1/events/${id}`);
  }

  /**
   * Creates an event. It starts as a draft and announces nothing.
   *
   * `capacity` null means no limit; the service refuses zero, which would be an
   * event nobody can attend.
   */
  createEvent(event: {
    title: string;
    description: string | null;
    startAt: string;
    endAt: string | null;
    venue: string | null;
    organizerType: OrganizerType;
    organizerId: string | null;
    registrationEnabled: boolean;
    capacity: number | null;
  }): Observable<SamaajEvent> {
    return this.http.post<SamaajEvent>('/v1/events', event);
  }

  /** Tells the Samaaj. Separate from creating, because it is a separate decision. */
  publishEvent(id: string): Observable<SamaajEvent> {
    return this.http.post<SamaajEvent>(`/v1/events/${id}/publish`, {});
  }

  /**
   * Calls an event off. The reason is required — people who rearranged their
   * day are told it — and a cancelled event cannot be republished.
   */
  cancelEvent(id: string, reason: string): Observable<SamaajEvent> {
    return this.http.post<SamaajEvent>(`/v1/events/${id}/cancel`, { reason });
  }

  /** Who is going, and who is waiting. Needs `Events.Publish`. */
  attendees(id: string): Observable<Attendee[]> {
    return this.http.get<Attendee[]>(`/v1/events/${id}/attendees`);
  }

  /**
   * The Samaaj's volunteer groups, used only to name an event's organiser.
   *
   * events-service stores an organiser as a type and an id; the group's name
   * lives in volunteer-groups-service. One call for the whole table rather than
   * one per row, the same approach every other cross-service name on this panel
   * takes.
   */
  organizerGroups(): Observable<OrganizerGroup[]> {
    return this.http.get<OrganizerGroup[]>('/v1/volunteer-groups/groups');
  }

  // ---- Boli --------------------------------------------------------------

  listOccasions(): Observable<Occasion[]> {
    return this.http.get<Occasion[]>('/v1/boli/occasions');
  }

  occasion(id: string): Observable<OccasionDetail> {
    return this.http.get<OccasionDetail>(`/v1/boli/occasions/${id}`);
  }

  createOccasion(
    title: string,
    description: string | null,
    occasionDate: string,
  ): Observable<Occasion> {
    return this.http.post<Occasion>('/v1/boli/occasions', {
      title,
      description,
      occasionDate,
    });
  }

  /** Forward only: Upcoming → Active → Closed. The service refuses backwards. */
  moveOccasion(id: string, status: OccasionStatus): Observable<Occasion> {
    return this.http.post<Occasion>(`/v1/boli/occasions/${id}/status`, { status });
  }

  /** A label the Samaaj reuses. One name per occasion, case-insensitively. */
  defineBoliType(
    occasionId: string,
    name: string,
    description: string | null,
  ): Observable<BoliType> {
    return this.http.post<BoliType>(`/v1/boli/occasions/${occasionId}/boli-types`, {
      name,
      description,
    });
  }

  /**
   * Opens a Boli for bidding.
   *
   * `startingAmount` and `minIncrement` are **paise**, as integers — see the
   * note on the Boli models. Sending rupees here would be off by a factor of a
   * hundred in a number the Samaaj collects against.
   */
  openBoli(
    occasionId: string,
    boli: {
      boliTypeId: string;
      title: string;
      startAt: string;
      endAt: string;
      startingAmount: number;
      minIncrement: number;
      eligibilityRule: string | null;
      autoExtendSeconds: number;
    },
  ): Observable<Boli> {
    return this.http.post<Boli>(`/v1/boli/occasions/${occasionId}/boli`, boli);
  }

  /** Idempotent, and it takes the bidding lock — closing races the last bids. */
  closeBoli(boliId: string): Observable<Boli> {
    return this.http.post<Boli>(`/v1/boli/boli/${boliId}/close`, {});
  }

  /**
   * Records who won, from the highest bid. Not yet announced.
   *
   * There is no winner parameter and there must not be: the service reads the
   * highest bid, so a recorded result cannot name somebody the append-only bid
   * history contradicts.
   */
  recordBoliResult(boliId: string): Observable<unknown> {
    return this.http.post(`/v1/boli/boli/${boliId}/result`, {});
  }

  /** Recorded and not yet announced. Needs `Boli.PublishResults`. */
  pendingBoliResults(): Observable<PendingResult[]> {
    return this.http.get<PendingResult[]>('/v1/boli/results/pending');
  }

  /** Announces a result. Irreversible through this API; publishing twice is safe. */
  publishBoliResult(boliId: string): Observable<unknown> {
    return this.http.post(`/v1/boli/boli/${boliId}/result/publish`, {});
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
