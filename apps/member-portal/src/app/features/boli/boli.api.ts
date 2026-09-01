import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Bid, Boli, BoliResult, Occasion, OccasionDetail, PlaceBidResult } from './boli.models';

/**
 * Every call this app makes to boli-service.
 *
 * Module-gated on `boli`, its own key.
 *
 * The manager's calls — announcing an occasion, defining types, opening and
 * closing a Boli, recording and publishing a result — need `Boli.Manage` or
 * `Boli.PublishResults`, which a Samaaj admin or a Boli manager holds. They
 * belong to the admin panel and are not here.
 *
 * **The paths really do read `/v1/boli/boli/{id}`.** The gateway routes
 * `/v1/boli/**` to the service and the resource under it is a Boli. It is the
 * same shape as `/v1/pathshala/pathshalas`, which reads better only because
 * "pathshala" has a plural and "Boli" does not.
 */
@Injectable({ providedIn: 'root' })
export class BoliApi {
  private readonly http = inject(HttpClient);

  /** This Samaaj's occasions, newest first. */
  occasions(): Observable<Occasion[]> {
    return this.http.get<Occasion[]>('/v1/boli/occasions');
  }

  occasion(id: string): Observable<OccasionDetail> {
    return this.http.get<OccasionDetail>(`/v1/boli/occasions/${id}`);
  }

  /** Every Boli taking bids right now. The wireframe's "Active Boli". */
  active(): Observable<Boli[]> {
    return this.http.get<Boli[]>('/v1/boli/boli/active');
  }

  get(id: string): Observable<Boli> {
    return this.http.get<Boli>(`/v1/boli/boli/${id}`);
  }

  /**
   * Places a bid. `amount` is in paise.
   *
   * Being outbid comes back as success with `accepted: false` and the amount
   * now needed — the caller must not treat it as an error.
   */
  bid(id: string, amount: number): Observable<PlaceBidResult> {
    return this.http.post<PlaceBidResult>(`/v1/boli/boli/${id}/bids`, { amount });
  }

  /** Amounts and times, highest first. Never who bid. */
  bids(id: string): Observable<Bid[]> {
    return this.http.get<Bid[]>(`/v1/boli/boli/${id}/bids`);
  }

  /** 404 until a result has been recorded. Names no winner until published. */
  result(id: string): Observable<BoliResult> {
    return this.http.get<BoliResult>(`/v1/boli/boli/${id}/result`);
  }

  /** Everything this Samaaj has announced, newest first. */
  publishedResults(): Observable<BoliResult[]> {
    return this.http.get<BoliResult[]>('/v1/boli/results');
  }
}
