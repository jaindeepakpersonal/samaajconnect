import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Biodata, BiodataFields, MatrimonyConsentNotice, MyBiodata, Proposal, SearchFilters } from './matrimony.models';

/**
 * Every call this app makes to matrimony-service.
 *
 * Module-gated on `matrimony`, off by default. Every write is self-scoped
 * (the same role list `UpdateProfileCommand` uses); every read is
 * `Members.Read`, already broadly granted.
 */
@Injectable({ providedIn: 'root' })
export class MatrimonyApi {
  private readonly http = inject(HttpClient);

  consentNotice(): Observable<MatrimonyConsentNotice> {
    return this.http.get<MatrimonyConsentNotice>('/v1/matrimony/consent-notice');
  }

  /**
   * The caller's own biodata, or `null` if they have never opted in.
   *
   * The service answers 204, not 200 with a null body - Angular's `HttpClient`
   * resolves a 204 to a `null` body itself, so this needs no special handling
   * beyond the nullable return type.
   */
  myBiodata(): Observable<MyBiodata | null> {
    return this.http.get<MyBiodata | null>('/v1/matrimony/biodata/me');
  }

  optIn(fields: BiodataFields, consentGiven: boolean, noticeVersion: string): Observable<MyBiodata> {
    return this.http.post<MyBiodata>('/v1/matrimony/biodata/me/opt-in', {
      ...fields,
      consentGiven,
      noticeVersion,
    });
  }

  update(fields: BiodataFields): Observable<MyBiodata> {
    return this.http.patch<MyBiodata>('/v1/matrimony/biodata/me', fields);
  }

  /** Deactivates - takes the caller's biodata out of search. Proposal history is kept. */
  withdrawConsent(): Observable<{ withdrawn: boolean }> {
    return this.http.delete<{ withdrawn: boolean }>('/v1/matrimony/biodata/me/consent');
  }

  search(filters: SearchFilters): Observable<Biodata[]> {
    let params = new HttpParams();

    if (filters.gender) {
      params = params.set('gender', filters.gender);
    }
    if (filters.minAge !== null) {
      params = params.set('minAge', filters.minAge);
    }
    if (filters.maxAge !== null) {
      params = params.set('maxAge', filters.maxAge);
    }
    if (filters.maritalStatusId) {
      params = params.set('maritalStatusId', filters.maritalStatusId);
    }
    if (filters.minHeightCm !== null) {
      params = params.set('minHeightCm', filters.minHeightCm);
    }
    if (filters.maxHeightCm !== null) {
      params = params.set('maxHeightCm', filters.maxHeightCm);
    }
    if (filters.diet) {
      params = params.set('diet', filters.diet);
    }
    if (filters.localityId) {
      params = params.set('localityId', filters.localityId);
    }
    if (filters.excludeOwnGautra) {
      params = params.set('excludeOwnGautra', true);
    }

    return this.http.get<Biodata[]>('/v1/matrimony/biodata/search', { params });
  }

  candidate(memberId: string): Observable<Biodata> {
    return this.http.get<Biodata>(`/v1/matrimony/biodata/${memberId}`);
  }

  sendProposal(toMemberId: string, message: string | null): Observable<Proposal> {
    return this.http.post<Proposal>('/v1/matrimony/proposals', { toMemberId, message });
  }

  sent(): Observable<Proposal[]> {
    return this.http.get<Proposal[]>('/v1/matrimony/proposals/sent');
  }

  received(): Observable<Proposal[]> {
    return this.http.get<Proposal[]>('/v1/matrimony/proposals/received');
  }

  /** The recipient only. */
  respond(proposalId: string, accept: boolean): Observable<Proposal> {
    return this.http.post<Proposal>(`/v1/matrimony/proposals/${proposalId}/respond`, { accept });
  }

  /** The sender only, while still Pending. */
  withdrawProposal(proposalId: string): Observable<{ withdrawn: boolean }> {
    return this.http.post<{ withdrawn: boolean }>(
      `/v1/matrimony/proposals/${proposalId}/withdraw`,
      {},
    );
  }
}
