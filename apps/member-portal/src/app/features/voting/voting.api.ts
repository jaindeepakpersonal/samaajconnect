import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Campaign,
  CampaignDetail,
  CampaignResult,
  NominateResult,
  VoteResult,
} from './voting.models';

/**
 * Every call this app makes to celebrity-voting-service.
 *
 * Module-gated on `celebrity-voting`, its own key.
 *
 * The organiser's calls - creating a campaign, moving its status, approving a
 * nomination, publishing the result - all need `CelebrityVoting.Configure`,
 * which is a Samaaj admin's. They belong to the admin panel and are not here.
 */
@Injectable({ providedIn: 'root' })
export class VotingApi {
  private readonly http = inject(HttpClient);

  /** This Samaaj's campaigns, each with this member's own vote on it. */
  list(): Observable<Campaign[]> {
    return this.http.get<Campaign[]>('/v1/celebrity-voting/campaigns');
  }

  get(id: string): Observable<CampaignDetail> {
    return this.http.get<CampaignDetail>(`/v1/celebrity-voting/campaigns/${id}`);
  }

  /**
   * Puts a member forward.
   *
   * A second nomination of the same person is reported as success with
   * `nominated: false` - the second nominator has done nothing wrong, and one
   * candidacy per member is what keeps a vote from splitting.
   */
  nominate(id: string, memberId: string, category: string | null): Observable<NominateResult> {
    return this.http.post<NominateResult>(
      `/v1/celebrity-voting/campaigns/${id}/candidates`,
      { memberId, category },
    );
  }

  /**
   * Casts this member's one vote.
   *
   * Voting twice comes back as success with `accepted: false`, carrying the
   * vote they already hold. The unique index on (campaign, voter) is what
   * enforces it; this call just reports the outcome.
   */
  vote(id: string, candidateId: string): Observable<VoteResult> {
    return this.http.post<VoteResult>(
      `/v1/celebrity-voting/campaigns/${id}/votes`,
      { candidateId },
    );
  }

  /** The published ranking, as frozen. 404 until it has been published. */
  result(id: string): Observable<CampaignResult> {
    return this.http.get<CampaignResult>(`/v1/celebrity-voting/campaigns/${id}/results`);
  }
}
