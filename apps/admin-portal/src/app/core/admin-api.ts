import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Paged } from '@samaajconnect/shared';
import { Observable, map } from 'rxjs';
import {
  ActivationCode,
  Attendee,
  AttendanceStatus,
  AttendanceSummary,
  AdminMember,
  AdminMemberGroup,
  AdminMemberHousehold,
  AdminUser,
  AssignRoleResult,
  AuditLogEntry,
  Boli,
  BoliType,
  Campaign,
  CampaignDetail,
  CampaignInput,
  CampaignResult,
  CampaignStatus,
  Election,
  ElectionDetail,
  ElectionInput,
  ElectionResult,
  ElectionStatus,
  FamilyOverview,
  Panel,
  ClassExam,
  Competition,
  CompetitionInput,
  CompetitionDetail,
  CompetitionResult,
  CompetitionStatus,
  ModerationDecision,
  ModerationQueueEntry,
  Enrolment,
  GroupStatus,
  Pathshala,
  PathshalaClass,
  PathshalaDetail,
  PathshalaTeacher,
  TeacherInput,
  PathshalaStudent,
  ProgressRow,
  ClassAttendance,
  ExamOverview,
  BidLedgerEntry,
  BoliResultView,
  DonationLedger,
  PathshalaDonation,
  DonationInput,
  DonationCorrection,
  DonationMode,
  PathshalaAnnouncement,
  Broadcast,
  BroadcastResult,
  ConversionRequest,
  CreateTenantRequest,
  InviteAdminRequest,
  InviteAdminResult,
  City,
  Gautra,
  GroupApplication,
  GroupDetail,
  GroupElection,
  GroupElectionDetail,
  GroupElectionInput,
  GroupElectionStatus,
  IssueReviewAccess,
  IssueStatus,
  Locality,
  LookupCategory,
  LookupItem,
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
  RegistrationSummary,
  RoleMatrix,
  Round,
  RoundDetail,
  SamaajEvent,
  EventScheduleItemInput,
  SecurityPolicy,
  SocialIssue,
  State,
  Tenant,
  TenantStatus,
  Trust,
  TrustCollection,
  TrustDonation,
  TrustDonationInput,
  TrustElection,
  TrustElectionDetail,
  TrustElectionInput,
  TrustElectionStatus,
  TrustExpense,
  TrustExpenseInput,
  TrustMember,
  TrustRole,
  TrustStatus,
  UserStatusResult,
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

  /** A volunteer group's banner photo. Multipart, like a logo; the service reads the type from the bytes. */
  uploadGroupPhoto(groupId: string, file: File): Observable<void> {
    const body = new FormData();
    body.append('file', file);

    return this.http.post<void>(`/v1/volunteer-groups/groups/${groupId}/photo`, body);
  }

  /** Takes a group's photo down. Doing it twice is success. */
  removeGroupPhoto(groupId: string): Observable<void> {
    return this.http.delete<void>(`/v1/volunteer-groups/groups/${groupId}/photo`);
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

  /**
   * One role per call: the screen is a checkbox, so this is one checkbox.
   * `localityId` is required when granting AreaAdmin and refused otherwise.
   */
  setRole(
    userId: string,
    role: string,
    granted: boolean,
    localityId?: string,
  ): Observable<AssignRoleResult> {
    return this.http.put<AssignRoleResult>(
      `/v1/identity/admins/${userId}/roles/${encodeURIComponent(role)}`,
      localityId ? { granted, localityId } : { granted },
    );
  }

  /**
   * Suspending re-asks for the caller's own password, the same asymmetry
   * `changeTenantStatus` draws between taking something out of service and
   * restoring it; reinstating does not. The server decides which, so
   * `password` is always sent and ignored where it is not needed.
   */
  setUserSuspension(
    userId: string,
    suspended: boolean,
    password?: string,
  ): Observable<UserStatusResult> {
    return this.http.put<UserStatusResult>(
      `/v1/identity/admins/${userId}/status`,
      { suspended, password },
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
  listMembers(): Observable<readonly { id: string; fullName: string }[]> {
    return this.http
      .get<Paged<{ id: string; fullName: string }>>('/v1/members?take=100')
      .pipe(map((page) => page.items));
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
  /**
   * This Samaaj's households with their members and minor children, newest
   * first. A locality narrows to households with a member living there; an
   * Area Admin must name their own (anything else is a 403).
   */
  listFamilies(localityId: string | null, skip = 0, take = 20): Observable<Paged<FamilyOverview>> {
    let params = new HttpParams().set('skip', skip).set('take', take);

    if (localityId) {
      params = params.set('localityId', localityId);
    }

    return this.http.get<Paged<FamilyOverview>>('/v1/families', { params });
  }

  searchMembers(
    term: string | null,
    localityId: string | null,
    skip = 0,
    take = 20,
    more: { readonly gautraId?: string | null; readonly professionId?: string | null } = {},
  ): Observable<Paged<AdminMember>> {
    let params = new HttpParams().set('skip', skip).set('take', take);

    if (more.gautraId) {
      params = params.set('gautraId', more.gautraId);
    }

    if (more.professionId) {
      params = params.set('professionId', more.professionId);
    }

    if (term && term.trim() !== '') {
      params = params.set('term', term.trim());
    }

    if (localityId) {
      params = params.set('localityId', localityId);
    }

    return this.http.get<Paged<AdminMember>>('/v1/members', { params });
  }

  /** The household a member belongs to, for their page. `household` is null when they are in none. */
  memberHousehold(id: string): Observable<AdminMemberHousehold> {
    return this.http.get<AdminMemberHousehold>(`/v1/members/${id}/household`);
  }

  /** The volunteer groups a member belongs to, and their role in each. */
  memberGroups(id: string): Observable<AdminMemberGroup[]> {
    return this.http.get<AdminMemberGroup[]>(`/v1/volunteer-groups/members/${id}/groups`);
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

  /**
   * `staffed` narrows to the Pathshalas the caller works at - what a teacher
   * sees. The service forces it for a teacher from outside the Samaaj anyway.
   */
  listPathshalas(staffed = false): Observable<Pathshala[]> {
    return this.http.get<Pathshala[]>(
      '/v1/pathshala/pathshalas',
      staffed ? { params: { staffed: true } } : {},
    );
  }

  /** `asStaff` answers 404 unless the caller works at this Pathshala. */
  pathshala(id: string, asStaff = false): Observable<PathshalaDetail> {
    return this.http.get<PathshalaDetail>(
      `/v1/pathshala/pathshalas/${id}`,
      asStaff ? { params: { asStaff: true } } : {},
    );
  }

  /**
   * Creates the master record. **Super Admin only** (`DATA-MODEL.md` §9): the
   * record belongs to the platform operator, and everything about running it
   * belongs to the Samaaj.
   */
  createPathshala(
    name: string,
    address: {
      addressLine: string | null;
      countryId: string | null;
      stateId: string | null;
      cityId: string | null;
      localityId: string | null;
    },
    contactPerson: string | null,
  ): Observable<Pathshala> {
    return this.http.post<Pathshala>('/v1/pathshala/pathshalas', {
      name,
      ...address,
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
  /**
   * Corrects a Pathshala's name, address and contact person, replacing all
   * three. `Pathshala.Manage`, so a Samaaj Admin can give a Pathshala created
   * before localities mattered the one that makes it an Area Admin's to run.
   */
  updatePathshala(
    id: string,
    name: string,
    address: {
      addressLine: string | null;
      countryId: string | null;
      stateId: string | null;
      cityId: string | null;
      localityId: string | null;
    },
    contactPerson: string | null,
  ): Observable<Pathshala> {
    return this.http.patch<Pathshala>(`/v1/pathshala/pathshalas/${id}`, {
      name,
      ...address,
      contactPerson,
    });
  }

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

  /** Corrects a session's label and dates. Which session is current does not change. 409 on a label already used. */
  updateSession(
    pathshalaId: string,
    sessionId: string,
    label: string,
    startDate: string,
    endDate: string,
  ): Observable<PathshalaDetail> {
    return this.http.patch<PathshalaDetail>(`/v1/pathshala/pathshalas/${pathshalaId}/sessions/${sessionId}`, {
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
   * Creates a group, optionally installing its president in the same act.
   *
   * The president may be left out: a group can exist before anyone is chosen to
   * lead it, and gets one later - an admin choosing an existing member, or a
   * "President" election. While it has none, a Samaaj admin runs it.
   */
  createGroup(
    name: string,
    description: string | null,
    focusArea: string | null,
    presidentMemberId: string | null,
    localityId: string | null = null,
  ): Observable<VolunteerGroup> {
    return this.http.post<VolunteerGroup>('/v1/volunteer-groups/groups', {
      name,
      description,
      focusArea,
      presidentMemberId,
      localityId,
    });
  }

  /**
   * Corrects a group's name, description, focus area or locality.
   *
   * Not the president, membership or status - each already has its own call.
   * The locality is always sent: omitted, it would clear the group's.
   */
  updateGroup(
    id: string,
    name: string,
    description: string | null,
    focusArea: string | null,
    localityId: string | null,
  ): Observable<VolunteerGroup> {
    return this.http.patch<VolunteerGroup>(`/v1/volunteer-groups/groups/${id}`, {
      name,
      description,
      focusArea,
      localityId,
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

  // ---- Volunteer group elections -----------------------------------------

  /** One group with its roster, and whether this caller may run its elections now. */
  groupDetail(id: string): Observable<GroupDetail> {
    return this.http.get<GroupDetail>(`/v1/volunteer-groups/groups/${id}`);
  }

  /**
   * A group's join requests. The service answers 404 to anybody who may not
   * decide them, and an administrator may only while the group has no president.
   */
  groupApplications(groupId: string): Observable<GroupApplication[]> {
    return this.http.get<GroupApplication[]>(`/v1/volunteer-groups/groups/${groupId}/applications`);
  }

  /** Accepts or turns down a request. Carries no position: positions come from elections. */
  decideGroupApplication(
    groupId: string,
    applicationId: string,
    accept: boolean,
  ): Observable<GroupApplication> {
    return this.http.post<GroupApplication>(
      `/v1/volunteer-groups/groups/${groupId}/applications/${applicationId}/decide`,
      { accept },
    );
  }

  /** A group's elections, newest first. */
  listGroupElections(groupId: string): Observable<GroupElection[]> {
    return this.http.get<GroupElection[]>(`/v1/volunteer-groups/groups/${groupId}/elections`);
  }

  /** Defines a volunteer role with the seats an election for it fills. Answers with the whole group. */
  defineGroupRole(groupId: string, title: string, seats: number): Observable<GroupDetail> {
    return this.http.post<GroupDetail>(`/v1/volunteer-groups/groups/${groupId}/roles`, { title, seats });
  }

  /** Renames or resizes a role. Not Volunteer or President, and never below its holders. */
  updateGroupRole(groupId: string, roleId: string, title: string, seats: number): Observable<GroupDetail> {
    return this.http.patch<GroupDetail>(`/v1/volunteer-groups/groups/${groupId}/roles/${roleId}`, {
      title,
      seats,
    });
  }

  /** Removes a role nobody holds and no election is deciding. */
  removeGroupRole(groupId: string, roleId: string): Observable<GroupDetail> {
    return this.http.delete<GroupDetail>(`/v1/volunteer-groups/groups/${groupId}/roles/${roleId}`);
  }

  /** Moves a member into a role - not President, which moves only with the presidency. */
  assignGroupRole(groupId: string, memberId: string, roleId: string): Observable<GroupDetail> {
    return this.http.put<GroupDetail>(`/v1/volunteer-groups/groups/${groupId}/members/${memberId}/role`, {
      roleId,
    });
  }

  /**
   * Sets an election up for one of the group's roles. It starts as a draft.
   * The president or an administrator of the group, at any time.
   */
  openGroupElection(groupId: string, election: GroupElectionInput): Observable<GroupElection> {
    return this.http.post<GroupElection>(
      `/v1/volunteer-groups/groups/${groupId}/elections`,
      election,
    );
  }

  groupElection(id: string): Observable<GroupElectionDetail> {
    return this.http.get<GroupElectionDetail>(`/v1/volunteer-groups/elections/${id}`);
  }

  /**
   * Corrects the role or windows - how nominations that closed too
   * early are reopened, since nothing here moves the election's own status.
   * Allowed in Draft or NominationsOpen; the service refuses it (409) from
   * VotingOpen on.
   */
  updateGroupElection(id: string, election: GroupElectionInput): Observable<GroupElection> {
    return this.http.patch<GroupElection>(`/v1/volunteer-groups/elections/${id}`, election);
  }

  /** Opens nominations, opens voting, or closes. Strictly forward. */
  moveGroupElection(id: string, status: GroupElectionStatus): Observable<GroupElection> {
    return this.http.post<GroupElection>(`/v1/volunteer-groups/elections/${id}/status`, { status });
  }

  /** Puts a nomination on the ballot, or takes it off before voting opens. */
  decideGroupCandidate(
    electionId: string,
    candidateId: string,
    approve: boolean,
  ): Observable<GroupElectionDetail> {
    return this.http.post<GroupElectionDetail>(
      `/v1/volunteer-groups/elections/${electionId}/candidates/${candidateId}/decide`,
      { approve },
    );
  }

  /** Freezes the winners and installs them in the role - the group's president, for a President election. */
  publishGroupElection(id: string): Observable<GroupElection> {
    return this.http.post<GroupElection>(`/v1/volunteer-groups/elections/${id}/publish`, {});
  }

  // ---- Trust ---------------------------------------------------------------

  /** This Samaaj's Trusts, optionally narrowed to one locality. */
  listTrusts(localityId?: string | null): Observable<Trust[]> {
    let params = new HttpParams();

    if (localityId) {
      params = params.set('localityId', localityId);
    }

    return this.http.get<Trust[]>('/v1/trust/trusts', { params });
  }

  /** Sets a Trust up for a locality. Exactly one per (Samaaj, Locality) - a second attempt is a 409. */
  createTrust(localityId: string, name: string): Observable<Trust> {
    return this.http.post<Trust>('/v1/trust/trusts', { localityId, name });
  }

  /** Corrects the name. A Trust's locality is not editable - see RenameTrustCommand. */
  renameTrust(id: string, name: string): Observable<Trust> {
    return this.http.patch<Trust>(`/v1/trust/trusts/${id}`, { name });
  }

  getTrust(id: string): Observable<Trust> {
    return this.http.get<Trust>(`/v1/trust/trusts/${id}`);
  }

  changeTrustStatus(id: string, status: TrustStatus): Observable<Trust> {
    return this.http.post<Trust>(`/v1/trust/trusts/${id}/status`, { status });
  }

  /** A Trust's roster, active members first. */
  listTrustMembers(trustId: string): Observable<TrustMember[]> {
    return this.http.get<TrustMember[]>(`/v1/trust/trusts/${trustId}/members`);
  }

  /**
   * Adds a member, or reactivates one previously removed - a repeat add of an
   * active member is a no-op. Trustee unless a role is named; 409 when it is full.
   */
  addTrustMember(trustId: string, memberId: string, roleId: string | null): Observable<TrustMember> {
    return this.http.post<TrustMember>(`/v1/trust/trusts/${trustId}/members`, { memberId, roleId });
  }

  /** Moves an active trustee into a role. 409 on a removed member, or a full role. */
  assignTrustRole(trustId: string, trustMemberId: string, roleId: string): Observable<TrustMember> {
    return this.http.put<TrustMember>(`/v1/trust/trusts/${trustId}/members/${trustMemberId}/role`, {
      roleId,
    });
  }

  /** A Trust's roles, Trustee first, each with its seats and how many hold it. */
  listTrustRoles(trustId: string): Observable<TrustRole[]> {
    return this.http.get<TrustRole[]>(`/v1/trust/trusts/${trustId}/roles`);
  }

  defineTrustRole(trustId: string, title: string, seats: number): Observable<TrustRole> {
    return this.http.post<TrustRole>(`/v1/trust/trusts/${trustId}/roles`, { title, seats });
  }

  /** Rename or resize. 409 below the current holders. */
  updateTrustRole(trustId: string, roleId: string, title: string, seats: number): Observable<TrustRole> {
    return this.http.put<TrustRole>(`/v1/trust/trusts/${trustId}/roles/${roleId}`, { title, seats });
  }

  /** 409 while anybody holds it, or an election for it is unpublished. */
  removeTrustRole(trustId: string, roleId: string): Observable<string> {
    return this.http.delete<string>(`/v1/trust/trusts/${trustId}/roles/${roleId}`);
  }

  /** Takes a member off the roster. Soft: a repeat removal is a no-op. */
  removeTrustMember(trustId: string, trustMemberId: string): Observable<TrustMember> {
    return this.http.delete<TrustMember>(`/v1/trust/trusts/${trustId}/members/${trustMemberId}`);
  }

  // ---- Trust elections -------------------------------------------------------

  /** This Trust's elections, newest first. */
  listTrustElections(trustId: string): Observable<TrustElection[]> {
    return this.http.get<TrustElection[]>(`/v1/trust/trusts/${trustId}/elections`);
  }

  /** Sets an election up for one of this Trust's roles, filling its seats. Starts as a draft. */
  openTrustElection(trustId: string, election: TrustElectionInput): Observable<TrustElection> {
    return this.http.post<TrustElection>(`/v1/trust/trusts/${trustId}/elections`, election);
  }

  getTrustElection(id: string): Observable<TrustElectionDetail> {
    return this.http.get<TrustElectionDetail>(`/v1/trust/elections/${id}`);
  }

  /** Opens nominations, opens voting, or closes. Strictly forward. */
  moveTrustElection(id: string, status: TrustElectionStatus): Observable<TrustElection> {
    return this.http.post<TrustElection>(`/v1/trust/elections/${id}/status`, { status });
  }

  /** Puts a nomination on the ballot, or takes it off before voting opens. */
  decideTrustCandidate(
    electionId: string,
    candidateId: string,
    approve: boolean,
  ): Observable<TrustElectionDetail> {
    return this.http.post<TrustElectionDetail>(
      `/v1/trust/elections/${electionId}/candidates/${candidateId}/decide`,
      { approve },
    );
  }

  /** Freezes the winner and installs them in the position, clearing any prior holder. */
  publishTrustElection(id: string): Observable<TrustElection> {
    return this.http.post<TrustElection>(`/v1/trust/elections/${id}/publish`, {});
  }

  // ---- Trust finance -------------------------------------------------------

  trustDonations(trustId: string): Observable<TrustDonation[]> {
    return this.http.get<TrustDonation[]>(`/v1/trust/trusts/${trustId}/donations`);
  }

  /** Records a pledge. It is given a receipt number when it is collected, not now. */
  recordTrustDonation(trustId: string, donation: TrustDonationInput): Observable<TrustDonation> {
    return this.http.post<TrustDonation>(`/v1/trust/trusts/${trustId}/donations`, donation);
  }

  /** Issues the Trust's next receipt number. 409 on a donation already collected. */
  collectTrustDonation(donationId: string): Observable<TrustDonation> {
    return this.http.post<TrustDonation>(`/v1/trust/donations/${donationId}/collect`, null);
  }

  trustExpenses(trustId: string): Observable<TrustExpense[]> {
    return this.http.get<TrustExpense[]>(`/v1/trust/trusts/${trustId}/expenses`);
  }

  recordTrustExpense(trustId: string, expense: TrustExpenseInput): Observable<TrustExpense> {
    return this.http.post<TrustExpense>(`/v1/trust/trusts/${trustId}/expenses`, expense);
  }

  /** Approving one already approved changes nothing and answers 200. */
  approveTrustExpense(expenseId: string): Observable<TrustExpense> {
    return this.http.post<TrustExpense>(`/v1/trust/expenses/${expenseId}/approve`, null);
  }

  trustCollections(trustId: string): Observable<TrustCollection[]> {
    return this.http.get<TrustCollection[]>(`/v1/trust/trusts/${trustId}/collections`);
  }

  /**
   * Sends the member who owes it an in-app reminder. At most once a day; 409
   * for a donor with no account, or once it is paid.
   */
  remindTrustPayer(collectionId: string): Observable<TrustCollection> {
    return this.http.post<TrustCollection>(`/v1/trust/collections/${collectionId}/remind`, null);
  }

  /** Records that an amount due was paid, and how. 409 once it has been. */
  recordTrustPayment(collectionId: string, mode: string): Observable<TrustCollection> {
    return this.http.post<TrustCollection>(`/v1/trust/collections/${collectionId}/payment`, { mode });
  }

  // ---- Social issues -----------------------------------------------------

  /**
   * Whether this administrator still reviews issues.
   *
   * True only while no Samaaj Panch is seated — the fallback that keeps every
   * issue reviewable before a Samaaj has elected one. Once a Panch exists the
   * queue is still readable (`canSeeQueue`), so an administrator can tell
   * whether it is being worked, but `canReview` goes false and this screen
   * stops offering any move.
   */
  issueReviewAccess(): Observable<IssueReviewAccess> {
    return this.http.get<IssueReviewAccess>('/v1/social-issues/review-access');
  }

  /** What is waiting on a decision, oldest first. */
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
  createCampaign(campaign: CampaignInput): Observable<Campaign> {
    return this.http.post<Campaign>('/v1/celebrity-voting/campaigns', campaign);
  }

  /**
   * Corrects a campaign's details, under the same rules as creating one. The
   * service refuses (409) once voting has opened: people have been told the
   * windows and whether the tally is live, and votes may exist.
   */
  updateCampaign(id: string, campaign: CampaignInput): Observable<Campaign> {
    return this.http.patch<Campaign>(`/v1/celebrity-voting/campaigns/${id}`, campaign);
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

  // ---- Samaaj Panch ------------------------------------------------------

  /** The seated Panch. A 404 means the panch module is switched off for this Samaaj. */
  panel(): Observable<Panel> {
    return this.http.get<Panel>('/v1/panch/panel');
  }

  /** Unseats one member. The seat stays empty until the next election. */
  removePanelMember(memberId: string): Observable<Panel> {
    return this.http.delete<Panel>(`/v1/panch/panel/${memberId}`);
  }

  listElections(): Observable<Election[]> {
    return this.http.get<Election[]>('/v1/panch/elections');
  }

  election(id: string): Observable<ElectionDetail> {
    return this.http.get<ElectionDetail>(`/v1/panch/elections/${id}`);
  }

  /** Sets an election up. It starts as a draft. */
  createElection(election: ElectionInput): Observable<Election> {
    return this.http.post<Election>('/v1/panch/elections', election);
  }

  /** Moves an election on one stage. Never backwards. */
  moveElection(id: string, status: ElectionStatus): Observable<Election> {
    return this.http.post<Election>(`/v1/panch/elections/${id}/status`, { status });
  }

  /** Puts a nomination on the ballot, or takes it off before voting opens. */
  decideElectionCandidate(
    electionId: string,
    candidateId: string,
    approve: boolean,
  ): Observable<ElectionDetail> {
    return this.http.post<ElectionDetail>(
      `/v1/panch/elections/${electionId}/candidates/${candidateId}/decide`,
      { approve },
    );
  }

  /** Freezes the ranking and seats the winners, replacing the current Panch. */
  publishElection(id: string): Observable<ElectionResult> {
    return this.http.post<ElectionResult>(`/v1/panch/elections/${id}/results`, {});
  }

  electionResults(id: string): Observable<ElectionResult> {
    return this.http.get<ElectionResult>(`/v1/panch/elections/${id}/results`);
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
  /** An event's cover photo. Multipart; the service reads the type from the bytes. Answers the whole event. */
  uploadEventPhoto(eventId: string, file: File): Observable<SamaajEvent> {
    const body = new FormData();
    body.append('file', file);

    return this.http.post<SamaajEvent>(`/v1/events/${eventId}/photo`, body);
  }

  /** Takes an event's photo down. Doing it twice is success. */
  removeEventPhoto(eventId: string): Observable<SamaajEvent> {
    return this.http.delete<SamaajEvent>(`/v1/events/${eventId}/photo`);
  }

  /** Adds a programme to an event's schedule; answers the whole event. */
  addEventScheduleItem(eventId: string, item: EventScheduleItemInput): Observable<SamaajEvent> {
    return this.http.post<SamaajEvent>(`/v1/events/${eventId}/schedule`, item);
  }

  /** Corrects one programme; answers the whole event. */
  updateEventScheduleItem(eventId: string, itemId: string, item: EventScheduleItemInput): Observable<SamaajEvent> {
    return this.http.put<SamaajEvent>(`/v1/events/${eventId}/schedule/${itemId}`, item);
  }

  /** Takes one programme off; answers the whole event. */
  removeEventScheduleItem(eventId: string, itemId: string): Observable<SamaajEvent> {
    return this.http.delete<SamaajEvent>(`/v1/events/${eventId}/schedule/${itemId}`);
  }

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
    eventTypeId: string | null;
    organizerType: OrganizerType;
    organizerId: string | null;
    registrationEnabled: boolean;
    capacity: number | null;
    localityId: string | null;
  }): Observable<SamaajEvent> {
    return this.http.post<SamaajEvent>('/v1/events', event);
  }

  /**
   * Corrects an event's own details. Allowed on a Draft or a Published
   * event, refused once it is Cancelled - there is nothing left to correct.
   * The organiser is not part of this call; reassigning it is a bigger
   * change than a correction and `UpdateEventCommand` does not take one.
   */
  updateEvent(
    id: string,
    event: {
      title: string;
      description: string | null;
      startAt: string;
      endAt: string | null;
      venue: string | null;
      eventTypeId: string | null;
      registrationEnabled: boolean;
      capacity: number | null;
      // Always sent: omitted, it would clear the event's locality.
      localityId: string | null;
    },
  ): Observable<SamaajEvent> {
    return this.http.patch<SamaajEvent>(`/v1/events/${id}`, event);
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

  /** Corrects an occasion. 409 once it is closed. */
  updateOccasion(
    id: string,
    title: string,
    description: string | null,
    occasionDate: string,
    localityId: string | null,
    venue: string | null = null,
    cityId: string | null = null,
  ): Observable<Occasion> {
    return this.http.patch<Occasion>(`/v1/boli/occasions/${id}`, {
      title,
      description,
      occasionDate,
      localityId,
      venue,
      cityId,
    });
  }

  createOccasion(
    title: string,
    description: string | null,
    occasionDate: string,
    localityId: string | null = null,
    venue: string | null = null,
    cityId: string | null = null,
  ): Observable<Occasion> {
    return this.http.post<Occasion>('/v1/boli/occasions', {
      localityId,
      title,
      description,
      occasionDate,
      venue,
      cityId,
    });
  }

  /** Every bid on a Boli with who placed it and when, highest first. `Boli.Manage` only. */
  bidLedger(boliId: string): Observable<BidLedgerEntry[]> {
    return this.http.get<BidLedgerEntry[]>(`/v1/boli/boli/${boliId}/bids/ledger`);
  }

  /** A Boli's result: 404 until one is recorded; the winner is named only once it is published. */
  boliResult(boliId: string): Observable<BoliResultView> {
    return this.http.get<BoliResultView>(`/v1/boli/boli/${boliId}/result`);
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

  /** Assigns one of the Pathshala's teacher records to a class, or takes it off. */
  assignTeacher(classId: string, teacherId: string, assign: boolean): Observable<PathshalaClass> {
    return this.http.post<PathshalaClass>(`/v1/pathshala/classes/${classId}/teachers`, {
      teacherId,
      assign,
    });
  }

  // ---- The Pathshala tabs --------------------------------------------------

  /** Its teachers, with contact details. Its administrators and teachers only. */
  pathshalaTeachers(pathshalaId: string): Observable<PathshalaTeacher[]> {
    return this.http.get<PathshalaTeacher[]>(`/v1/pathshala/pathshalas/${pathshalaId}/teachers`);
  }

  /** A teacher need not be a member. `userId` links their login, if any. */
  addTeacher(pathshalaId: string, teacher: TeacherInput): Observable<PathshalaTeacher> {
    return this.http.post<PathshalaTeacher>(`/v1/pathshala/pathshalas/${pathshalaId}/teachers`, teacher);
  }

  updateTeacher(pathshalaId: string, teacherId: string, teacher: TeacherInput): Observable<PathshalaTeacher> {
    return this.http.patch<PathshalaTeacher>(
      `/v1/pathshala/pathshalas/${pathshalaId}/teachers/${teacherId}`,
      teacher,
    );
  }

  /** Removes a teacher and takes them off every class. The registers they marked stay. */
  removeTeacher(pathshalaId: string, teacherId: string): Observable<string> {
    return this.http.delete<string>(`/v1/pathshala/pathshalas/${pathshalaId}/teachers/${teacherId}`);
  }

  /** Placed children (a teacher: their classes) and, for administrators, waiting requests. */
  pathshalaStudents(pathshalaId: string): Observable<PathshalaStudent[]> {
    return this.http.get<PathshalaStudent[]>(`/v1/pathshala/pathshalas/${pathshalaId}/students`);
  }

  progressReport(pathshalaId: string): Observable<ProgressRow[]> {
    return this.http.get<ProgressRow[]>(`/v1/pathshala/pathshalas/${pathshalaId}/progress`);
  }

  attendanceOverview(pathshalaId: string): Observable<ClassAttendance[]> {
    return this.http.get<ClassAttendance[]>(`/v1/pathshala/pathshalas/${pathshalaId}/attendance`);
  }

  pathshalaExams(pathshalaId: string): Observable<ExamOverview[]> {
    return this.http.get<ExamOverview[]>(`/v1/pathshala/pathshalas/${pathshalaId}/exams`);
  }

  /** The donation ledger. Administrators only - teachers get a 403. */
  donations(pathshalaId: string): Observable<DonationLedger> {
    return this.http.get<DonationLedger>(`/v1/pathshala/pathshalas/${pathshalaId}/donations`);
  }

  /**
   * Records a donation - paid, with its receipt number, or announced
   * (`paid: false`) with none until it is marked paid. `amount` is integer paise.
   */
  recordDonation(pathshalaId: string, donation: DonationInput): Observable<PathshalaDonation> {
    return this.http.post<PathshalaDonation>(`/v1/pathshala/pathshalas/${pathshalaId}/donations`, donation);
  }

  /** Corrects an entry. A pledge stays a pledge; a paid one needs its mode and paid date. */
  updateDonation(donationId: string, correction: DonationCorrection): Observable<PathshalaDonation> {
    return this.http.patch<PathshalaDonation>(`/v1/pathshala/donations/${donationId}`, correction);
  }

  /** An announced donation was paid: issues its receipt number. 409 once paid. */
  markDonationPaid(donationId: string, mode: DonationMode, paidOn: string): Observable<PathshalaDonation> {
    return this.http.post<PathshalaDonation>(`/v1/pathshala/donations/${donationId}/paid`, { mode, paidOn });
  }

  /**
   * Deletes a paid entry from the ledger and its totals, with a reason. The
   * service keeps the row, so its receipt number is never reissued. 409 on an
   * announcement - erase that with `deleteDonation`.
   */
  voidDonation(donationId: string, reason: string): Observable<PathshalaDonation> {
    return this.http.post<PathshalaDonation>(`/v1/pathshala/donations/${donationId}/void`, { reason });
  }

  /** Erases an announcement not yet paid; answers its id. 409 once it has a receipt. */
  deleteDonation(donationId: string): Observable<string> {
    return this.http.delete<string>(`/v1/pathshala/donations/${donationId}`);
  }

  announcements(pathshalaId: string): Observable<PathshalaAnnouncement[]> {
    return this.http.get<PathshalaAnnouncement[]>(`/v1/pathshala/pathshalas/${pathshalaId}/announcements`);
  }

  /** To the parents of every child with a place or a request; each gets an in-app notification. */
  sendAnnouncement(pathshalaId: string, title: string, body: string): Observable<PathshalaAnnouncement> {
    return this.http.post<PathshalaAnnouncement>(`/v1/pathshala/pathshalas/${pathshalaId}/announcements`, {
      title,
      body,
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

  // ---- Reports & Analytics ------------------------------------------------

  /** Registrations in the current 30-day window against the 30 days before it. */
  registrationSummary(): Observable<RegistrationSummary> {
    return this.http.get<RegistrationSummary>('/v1/members/registration-summary');
  }

  /** Present/absent/excused across every class this Samaaj runs. */
  attendanceSummary(): Observable<AttendanceSummary> {
    return this.http.get<AttendanceSummary>('/v1/pathshala/attendance-summary');
  }

  // ---- Settings: Security policy -------------------------------------------

  securityPolicy(tenantId: string): Observable<SecurityPolicy> {
    return this.http.get<SecurityPolicy>(`/v1/identity/tenants/${tenantId}/security-policy`);
  }

  /** A null field clears that override back to the platform default. */
  setSecurityPolicy(tenantId: string, policy: SecurityPolicy): Observable<SecurityPolicy> {
    return this.http.put<SecurityPolicy>(
      `/v1/identity/tenants/${tenantId}/security-policy`, policy,
    );
  }

  // ---- Master data --------------------------------------------------------
  //
  // Platform infrastructure, never module-gated (root CLAUDE.md §8) - a Samaaj
  // cannot switch off the dropdowns its own registration form depends on.
  // Country/ProfessionType/Gender/MaritalStatus/City/Locality are
  // `MasterData.Manage`, SuperAdmin only. Gautra is `Gautra.Manage`, a Samaaj
  // Admin's own list (or a Super Admin's, once a Samaaj is chosen).

  listLookupItems(category: LookupCategory, includeInactive = false): Observable<LookupItem[]> {
    const params = new HttpParams().set('category', category).set('includeInactive', includeInactive);

    return this.http.get<LookupItem[]>('/v1/master-data/lookup-items', { params });
  }

  createLookupItem(
    category: LookupCategory,
    code: string,
    label: string,
    sortOrder: number,
  ): Observable<LookupItem> {
    return this.http.post<LookupItem>('/v1/master-data/lookup-items', {
      category,
      code,
      label,
      sortOrder,
    });
  }

  /** Code and category are fixed once created - only the label and sort order move. */
  updateLookupItem(id: string, label: string, sortOrder: number): Observable<LookupItem> {
    return this.http.put<LookupItem>(`/v1/master-data/lookup-items/${id}`, { label, sortOrder });
  }

  deactivateLookupItem(id: string): Observable<LookupItem> {
    return this.http.post<LookupItem>(`/v1/master-data/lookup-items/${id}/deactivate`, {});
  }

  listStates(countryId?: string | null, includeInactive = false): Observable<State[]> {
    let params = new HttpParams().set('includeInactive', includeInactive);

    if (countryId) {
      params = params.set('countryId', countryId);
    }

    return this.http.get<State[]>('/v1/master-data/states', { params });
  }

  createState(countryId: string, name: string): Observable<State> {
    return this.http.post<State>('/v1/master-data/states', { countryId, name });
  }

  updateState(id: string, name: string): Observable<State> {
    return this.http.put<State>(`/v1/master-data/states/${id}`, { name });
  }

  deactivateState(id: string): Observable<State> {
    return this.http.post<State>(`/v1/master-data/states/${id}/deactivate`, {});
  }

  listCities(
    countryId?: string | null,
    stateId?: string | null,
    includeInactive = false,
  ): Observable<City[]> {
    let params = new HttpParams().set('includeInactive', includeInactive);

    if (countryId) {
      params = params.set('countryId', countryId);
    }

    if (stateId) {
      params = params.set('stateId', stateId);
    }

    return this.http.get<City[]>('/v1/master-data/cities', { params });
  }

  createCity(countryId: string, stateId: string | null, name: string): Observable<City> {
    return this.http.post<City>('/v1/master-data/cities', { countryId, stateId, name });
  }

  updateCity(id: string, stateId: string | null, name: string): Observable<City> {
    return this.http.put<City>(`/v1/master-data/cities/${id}`, { stateId, name });
  }

  deactivateCity(id: string): Observable<City> {
    return this.http.post<City>(`/v1/master-data/cities/${id}/deactivate`, {});
  }

  listLocalities(cityId?: string | null, includeInactive = false): Observable<Locality[]> {
    let params = new HttpParams().set('includeInactive', includeInactive);

    if (cityId) {
      params = params.set('cityId', cityId);
    }

    return this.http.get<Locality[]>('/v1/master-data/localities', { params });
  }

  createLocality(cityId: string, name: string): Observable<Locality> {
    return this.http.post<Locality>('/v1/master-data/localities', { cityId, name });
  }

  updateLocality(id: string, name: string): Observable<Locality> {
    return this.http.put<Locality>(`/v1/master-data/localities/${id}`, { name });
  }

  deactivateLocality(id: string): Observable<Locality> {
    return this.http.post<Locality>(`/v1/master-data/localities/${id}/deactivate`, {});
  }

  /** This Samaaj's own list, resolved from `X-Tenant-Id` (or the Super Admin's chosen scope). */
  listGautras(includeInactive = false): Observable<Gautra[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);

    return this.http.get<Gautra[]>('/v1/master-data/gautras', { params });
  }

  createGautra(name: string): Observable<Gautra> {
    return this.http.post<Gautra>('/v1/master-data/gautras', { name });
  }

  updateGautra(id: string, name: string): Observable<Gautra> {
    return this.http.put<Gautra>(`/v1/master-data/gautras/${id}`, { name });
  }

  deactivateGautra(id: string): Observable<Gautra> {
    return this.http.post<Gautra>(`/v1/master-data/gautras/${id}/deactivate`, {});
  }

  // ---- Competitions --------------------------------------------------------

  listCompetitions(): Observable<Competition[]> {
    return this.http.get<Competition[]>('/v1/competitions');
  }

  /** Rounds, registration count, judges, and whether the caller may manage it. */
  competition(id: string): Observable<CompetitionDetail> {
    return this.http.get<CompetitionDetail>(`/v1/competitions/${id}`);
  }

  /** Creates a competition. It starts as a draft until registration is opened. */
  /** Corrects a competition. Draft or taking registrations only; 409 after. */
  updateCompetition(id: string, competition: CompetitionInput): Observable<Competition> {
    return this.http.patch<Competition>(`/v1/competitions/${id}`, competition);
  }

  createCompetition(competition: CompetitionInput): Observable<Competition> {
    return this.http.post<Competition>('/v1/competitions', competition);
  }

  /** The competition's cover photo, replacing any - at any stage. Answers the competition. */
  uploadCompetitionPhoto(competitionId: string, file: File): Observable<Competition> {
    const body = new FormData();
    body.append('file', file);

    return this.http.post<Competition>(`/v1/competitions/${competitionId}/photo`, body);
  }

  /** Takes the competition's photo down. Doing it twice is success. */
  removeCompetitionPhoto(competitionId: string): Observable<Competition> {
    return this.http.delete<Competition>(`/v1/competitions/${competitionId}/photo`);
  }

  /**
   * Moves a competition on one stage.
   *
   * Draft → RegistrationOpen → RegistrationClosed → InProgress → Closed, and
   * never backwards.
   */
  moveCompetition(id: string, status: CompetitionStatus): Observable<Competition> {
    return this.http.post<Competition>(`/v1/competitions/${id}/status`, { status });
  }

  /** Assigns a member as a judge, or removes one. One call for both directions. */
  assignJudge(competitionId: string, memberId: string, assign: boolean): Observable<unknown> {
    return this.http.post(`/v1/competitions/${competitionId}/judges`, { memberId, assign });
  }

  /**
   * Opens a new round.
   *
   * There is no automatic advancement between rounds - entries are created one
   * at a time, in every round including the first.
   */
  createRound(competitionId: string, name: string): Observable<Round> {
    return this.http.post<Round>(`/v1/competitions/${competitionId}/rounds`, { name });
  }

  /** A round's entries. Marks are visible only to the organiser or an assigned judge. */
  round(competitionId: string, roundId: string): Observable<RoundDetail> {
    return this.http.get<RoundDetail>(`/v1/competitions/${competitionId}/rounds/${roundId}`);
  }

  /** Closes a round. After this, no new entries or scores are accepted. */
  closeRound(competitionId: string, roundId: string): Observable<Round> {
    return this.http.post<Round>(`/v1/competitions/${competitionId}/rounds/${roundId}/close`, {});
  }

  /** Puts one registered participant into a round. */
  createEntry(
    competitionId: string,
    roundId: string,
    participantMemberId: string,
  ): Observable<unknown> {
    return this.http.post(
      `/v1/competitions/${competitionId}/rounds/${roundId}/entries`,
      { participantMemberId },
    );
  }

  /**
   * Computes a round's ranking and freezes it. Only from a closed round.
   *
   * Publishing again returns the same frozen result rather than an error -
   * unlike celebrity-voting's campaign result, a repeat call here is not
   * refused, because there is nothing destructive about asking twice.
   */
  publishRoundResults(competitionId: string, roundId: string): Observable<CompetitionResult> {
    return this.http.post<CompetitionResult>(
      `/v1/competitions/${competitionId}/rounds/${roundId}/results`,
      {},
    );
  }

  /** The frozen ranking. 404 until it has been published. */
  roundResult(competitionId: string, roundId: string): Observable<CompetitionResult> {
    return this.http.get<CompetitionResult>(
      `/v1/competitions/${competitionId}/rounds/${roundId}/results`,
    );
  }
}
