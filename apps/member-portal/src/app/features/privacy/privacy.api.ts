import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import {
  ConsentNotice,
  ConsentState,
  EraseResult,
  FullExport,
  IdentityExport,
  ServiceExport,
} from './privacy.models';

/**
 * The DPDP rights: what was agreed to, a copy of it, and erasure.
 *
 * None of these is module-gated. A right under the Act does not depend on
 * which modules a Samaaj has switched on.
 */
@Injectable({ providedIn: 'root' })
export class PrivacyApi {
  private readonly http = inject(HttpClient);

  /** The notice as it stands, for the words beside each consent. */
  notice(): Observable<ConsentNotice> {
    return this.http.get<ConsentNotice>('/v1/identity/consent-notice');
  }

  /**
   * Everything identity holds, including where each consent stands now.
   *
   * There is no separate "read my consents" endpoint, and this is the reason
   * there does not need to be: the export already carries `currentConsents`
   * because section 11 requires it to.
   */
  identityExport(): Observable<IdentityExport> {
    return this.http.get<IdentityExport>('/v1/identity/me/data-export');
  }

  /**
   * Withdraws one consent.
   *
   * No confirmation step and no reason field: section 6(4) requires
   * withdrawing to be as easy as giving, and giving was a tick during
   * registration. Answers with where every purpose now stands.
   */
  withdraw(purpose: string): Observable<ConsentState[]> {
    return this.http.post<ConsentState[]>(
      `/v1/identity/me/consents/${purpose}/withdraw`,
      {},
    );
  }

  /**
   * The whole copy, assembled here from the three services that hold it.
   *
   * The platform has no single export endpoint deliberately: a member's data is
   * spread across identity, member-family and audit, and having one service
   * reach synchronously into the others would undo the service boundaries for
   * something used a handful of times a year. The client is the right place to
   * put them back together, because the client is the only party that is
   * already authenticated to all three.
   *
   * A service that answers with nothing - a member with no family record, an
   * account with no audit trail - contributes a note rather than failing the
   * export. A partial copy delivered is worth more than a complete one refused.
   */
  fullExport(): Observable<FullExport> {
    return forkJoin({
      identity: this.one('/v1/identity/me/data-export', 'identity-tenant-service'),
      memberFamily: this.one('/v1/members/me/data-export', 'member-family-service'),
      audit: this.one('/v1/audit/me/data-export', 'audit-notification-service'),
    }).pipe(
      map(({ identity, memberFamily, audit }) => ({
        exportedAt: new Date().toISOString(),
        platform: 'samaajconnect',
        note:
          'Assembled in your browser from each service that holds part of your data. ' +
          'Provided under section 11 of the Digital Personal Data Protection Act, 2023.',
        services: [identity, memberFamily, audit],
      })),
    );
  }

  /**
   * Erases the account.
   *
   * The password proves the person at the keyboard is the account holder,
   * before an irreversible act. A wrong one answers **403 `Auth.StepUpFailed`**
   * and not 401 - deliberately, because the auth interceptor renews on a 401
   * and retries, which on this endpoint would submit the erasure a second time
   * because somebody mistyped.
   */
  erase(password: string): Observable<EraseResult> {
    return this.http.post<EraseResult>('/v1/identity/me/erase', { password });
  }

  private one(url: string, service: string): Observable<ServiceExport> {
    return this.http.get<ServiceExport>(url).pipe(
      catchError(() =>
        of({
          service,
          note: 'This service returned nothing for your account.',
        } as ServiceExport),
      ),
    );
  }
}
