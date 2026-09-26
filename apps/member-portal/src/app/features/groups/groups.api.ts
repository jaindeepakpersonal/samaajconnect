import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CastVoteResult,
  Election,
  ElectionDetail,
  GroupApplication,
  GroupDetail,
  NominateResult,
  VolunteerGroup,
} from './groups.models';

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

  /**
   * Accepts or rejects an application. Carries no position - a new member's
   * position comes from an election now, or a later {@link setPosition} call,
   * never from the accept itself.
   */
  decide(id: string, applicationId: string, accept: boolean): Observable<GroupApplication> {
    return this.http.post<GroupApplication>(
      `/v1/volunteer-groups/groups/${id}/applications/${applicationId}/decide`,
      { accept },
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

  /**
   * Removes a member from the group. Never the president themselves - the
   * service refuses that with a 409, since a group whose president is not in
   * it has nobody able to decide its applications.
   */
  removeMember(id: string, memberId: string): Observable<GroupDetail> {
    return this.http.delete<GroupDetail>(
      `/v1/volunteer-groups/groups/${id}/members/${memberId}`,
    );
  }

  // ---- Elections -----------------------------------------------------------

  /** This group's elections, newest first, each with the asking member's own vote. */
  listElections(groupId: string): Observable<Election[]> {
    return this.http.get<Election[]>(`/v1/volunteer-groups/groups/${groupId}/elections`);
  }

  /** The president sets an election up for a position. It stays a draft until opened. */
  openElection(
    groupId: string,
    positionTitle: string,
    nominationStartAt: string,
    nominationEndAt: string,
    votingStartAt: string,
    votingEndAt: string,
  ): Observable<Election> {
    return this.http.post<Election>(`/v1/volunteer-groups/groups/${groupId}/elections`, {
      positionTitle,
      nominationStartAt,
      nominationEndAt,
      votingStartAt,
      votingEndAt,
    });
  }

  getElection(id: string): Observable<ElectionDetail> {
    return this.http.get<ElectionDetail>(`/v1/volunteer-groups/elections/${id}`);
  }

  /**
   * Corrects the position title or windows - how nominations that closed too
   * early are reopened, since nothing here moves the election's own status.
   * Allowed in Draft or NominationsOpen; the service refuses it (409) from
   * VotingOpen on.
   */
  updateElection(
    id: string,
    positionTitle: string,
    nominationStartAt: string,
    nominationEndAt: string,
    votingStartAt: string,
    votingEndAt: string,
  ): Observable<Election> {
    return this.http.patch<Election>(`/v1/volunteer-groups/elections/${id}`, {
      positionTitle,
      nominationStartAt,
      nominationEndAt,
      votingStartAt,
      votingEndAt,
    });
  }

  /** Opens nominations, opens voting, or closes. Strictly forward. */
  moveElection(id: string, status: string): Observable<Election> {
    return this.http.post<Election>(`/v1/volunteer-groups/elections/${id}/status`, { status });
  }

  /** Puts a group member forward. The president decides whether they reach the ballot. */
  nominate(id: string, memberId: string): Observable<NominateResult> {
    return this.http.post<NominateResult>(`/v1/volunteer-groups/elections/${id}/candidates`, {
      memberId,
    });
  }

  /** The president puts a nomination on the ballot, or removes it before voting opens. */
  decideCandidate(id: string, candidateId: string, approve: boolean): Observable<ElectionDetail> {
    return this.http.post<ElectionDetail>(
      `/v1/volunteer-groups/elections/${id}/candidates/${candidateId}/decide`,
      { approve },
    );
  }

  /**
   * Casts this member's one vote. Voting twice is reported as success with
   * `accepted: false` - the unique index, not this call, is what enforces it.
   */
  castVote(id: string, candidateId: string): Observable<CastVoteResult> {
    return this.http.post<CastVoteResult>(`/v1/volunteer-groups/elections/${id}/votes`, {
      candidateId,
    });
  }

  /** The president picks the winner, freezes it, and installs them in the position. */
  publishElectionResults(id: string): Observable<Election> {
    return this.http.post<Election>(`/v1/volunteer-groups/elections/${id}/publish`, {});
  }
}
