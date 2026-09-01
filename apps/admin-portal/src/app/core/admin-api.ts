import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ActivationCode,
  AdminUser,
  AssignRoleResult,
  AuditLogEntry,
  ModerationDecision,
  ModerationQueueEntry,
  Broadcast,
  BroadcastResult,
  ConversionRequest,
  CreateTenantRequest,
  InviteAdminRequest,
  InviteAdminResult,
  ModuleDescriptor,
  PendingActivation,
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
}
