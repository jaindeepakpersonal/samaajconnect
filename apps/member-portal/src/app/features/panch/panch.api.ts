import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BallotResult,
  Election,
  ElectionDetail,
  ElectionResult,
  NominateResult,
  Panel,
} from './panch.models';

/**
 * Every call this app makes to panch-service.
 *
 * Module-gated on `panch`, its own key.
 *
 * The organiser's calls - setting an election up, moving its stage, approving a
 * nomination, publishing the result, removing a panel member - all need
 * `Panch.Manage`, which is a Samaaj admin's. They belong to the admin panel and
 * are not here.
 */
@Injectable({ providedIn: 'root' })
export class PanchApi {
  private readonly http = inject(HttpClient);

  /** The seated Panch, and whether this member sits on it. */
  panel(): Observable<Panel> {
    return this.http.get<Panel>('/v1/panch/panel');
  }

  /** This Samaaj's elections, each with this member's own ballot. */
  list(): Observable<Election[]> {
    return this.http.get<Election[]>('/v1/panch/elections');
  }

  get(id: string): Observable<ElectionDetail> {
    return this.http.get<ElectionDetail>(`/v1/panch/elections/${id}`);
  }

  /**
   * Puts a member forward. A repeat is success with `nominated: false`: the
   * second nominator has done nothing wrong, and one candidacy per member keeps
   * a vote from splitting.
   */
  nominate(id: string, memberId: string): Observable<NominateResult> {
    return this.http.post<NominateResult>(`/v1/panch/elections/${id}/candidates`, { memberId });
  }

  /**
   * Casts this member's one ballot: up to ten candidates, final.
   *
   * A second ballot is reported as success with `accepted: false` and the
   * choices already held - the unique index on (election, voter) is what
   * prevents it, not this call.
   */
  castBallot(id: string, candidateIds: readonly string[]): Observable<BallotResult> {
    return this.http.post<BallotResult>(`/v1/panch/elections/${id}/ballots`, { candidateIds });
  }

  /** The published ranking and who was seated. 404 until published. */
  result(id: string): Observable<ElectionResult> {
    return this.http.get<ElectionResult>(`/v1/panch/elections/${id}/results`);
  }
}
