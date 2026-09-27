import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CastTrustVoteResult,
  NominateTrustCandidateResult,
  Trust,
  TrustElection,
  TrustElectionDetail,
  TrustDue,
  TrustMember,
} from './trust.models';

/**
 * Every call this app makes to trust-service.
 *
 * Module-gated on `trust`, its own key.
 *
 * Membership is admin-curated (root `CLAUDE.md`'s "add/update/delete trust
 * members" - confirmed with the user rather than derived from a member's
 * own locality), and running an election - opening it, approving a
 * nomination, publishing - needs `Trust.Manage`, a Samaaj admin's. Those
 * calls belong to the admin panel and are not here. This app only reads,
 * nominates and votes.
 */
@Injectable({ providedIn: 'root' })
export class TrustApi {
  private readonly http = inject(HttpClient);

  /**
   * What the signed-in member still owes to a Trust, oldest first. Only ever
   * their own: the service reads the payer off the token, not a parameter.
   */
  myDues(): Observable<TrustDue[]> {
    return this.http.get<TrustDue[]>('/v1/trust/collections/mine');
  }

  /** This Samaaj's Trusts, optionally narrowed to one locality. */
  list(localityId?: string | null): Observable<Trust[]> {
    let params = new HttpParams();

    if (localityId) {
      params = params.set('localityId', localityId);
    }

    return this.http.get<Trust[]>('/v1/trust/trusts', { params });
  }

  get(id: string): Observable<Trust> {
    return this.http.get<Trust>(`/v1/trust/trusts/${id}`);
  }

  /** A Trust's roster, active members first - this is also the electorate for its elections. */
  members(trustId: string): Observable<TrustMember[]> {
    return this.http.get<TrustMember[]>(`/v1/trust/trusts/${trustId}/members`);
  }

  /** A Trust's elections, newest first, each with this member's own ballot. */
  elections(trustId: string): Observable<TrustElection[]> {
    return this.http.get<TrustElection[]>(`/v1/trust/trusts/${trustId}/elections`);
  }

  election(id: string): Observable<TrustElectionDetail> {
    return this.http.get<TrustElectionDetail>(`/v1/trust/elections/${id}`);
  }

  /**
   * Puts a fellow active trustee forward. A repeat is success with
   * `nominated: false` - the second nominator has done nothing wrong, and one
   * candidacy per member keeps a vote from splitting.
   */
  nominate(electionId: string, memberId: string): Observable<NominateTrustCandidateResult> {
    return this.http.post<NominateTrustCandidateResult>(
      `/v1/trust/elections/${electionId}/candidates`,
      { memberId },
    );
  }

  /**
   * Casts this member's one ballot: up to the election's seats. A second ballot
   * is reported as success with `accepted: false` - the unique indexes are
   * what prevent it, not this call. Self-votes are refused.
   */
  castVote(electionId: string, candidateIds: readonly string[]): Observable<CastTrustVoteResult> {
    return this.http.post<CastTrustVoteResult>(`/v1/trust/elections/${electionId}/votes`, {
      candidateIds,
    });
  }
}
