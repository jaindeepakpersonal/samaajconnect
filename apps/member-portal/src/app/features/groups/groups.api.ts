import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { GroupApplication, GroupDetail, VolunteerGroup } from './groups.models';

/**
 * Every call this app makes to volunteer-groups-service.
 *
 * Module-gated on `community`. Creating a group and activating or deactivating
 * one need `VolunteerGroups.Manage`, which is a Samaaj admin's, so they are not
 * here - they belong to the admin panel.
 *
 * The president's calls *are* here, because a president is an ordinary member
 * who happens to lead a group. They are gated on `VolunteerGroups.Lead`, which
 * every member holds and which grants nothing until they are actually a
 * group's president - the service checks that against its own data. So the
 * portal offers them when `iAmThePresident` says to, and the service is what
 * decides.
 */
@Injectable({ providedIn: 'root' })
export class GroupsApi {
  private readonly http = inject(HttpClient);

  /** This Samaaj's groups, each with the asking member's standing. */
  list(): Observable<VolunteerGroup[]> {
    return this.http.get<VolunteerGroup[]>('/v1/volunteer-groups/groups');
  }

  get(id: string): Observable<GroupDetail> {
    return this.http.get<GroupDetail>(`/v1/volunteer-groups/groups/${id}`);
  }

  /** Asks to join. The president decides. */
  apply(id: string, note: string | null): Observable<GroupApplication> {
    return this.http.post<GroupApplication>(`/v1/volunteer-groups/groups/${id}/applications`, {
      note,
    });
  }

  /**
   * The president's review queue.
   *
   * Answers 404 rather than 403 to anyone who is not this group's president -
   * a 403 would confirm the group exists and has applications pending. The
   * screen only asks when it has been told the reader is the president.
   */
  applications(id: string): Observable<GroupApplication[]> {
    return this.http.get<GroupApplication[]>(`/v1/volunteer-groups/groups/${id}/applications`);
  }

  decide(
    id: string,
    applicationId: string,
    accept: boolean,
    rolePosition: string | null,
  ): Observable<GroupApplication> {
    return this.http.post<GroupApplication>(
      `/v1/volunteer-groups/groups/${id}/applications/${applicationId}/decide`,
      { accept, rolePosition },
    );
  }

  /** Gives a member a position in the group, or clears it. */
  setPosition(
    id: string,
    memberId: string,
    rolePosition: string | null,
  ): Observable<GroupDetail> {
    return this.http.put<GroupDetail>(
      `/v1/volunteer-groups/groups/${id}/members/${memberId}/position`,
      { rolePosition },
    );
  }
}
