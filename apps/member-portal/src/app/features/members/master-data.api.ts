import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { City, Gautra, Locality, LookupItem } from './members.models';

/**
 * Every call this app makes to master-data-service, first wired up for My
 * Profile's Gautra/Qualification dropdowns and its City → Locality address
 * cascade. admin-portal already calls the same endpoints for its own
 * management screen; this is the read side member-portal needed and never
 * had before.
 *
 * All four reads are anonymous on the service's own side - a member picks
 * these during registration, before they hold a token - but every call here
 * still goes out through the signed-in `HttpClient`, the same as any other
 * request this app makes.
 */
@Injectable({ providedIn: 'root' })
export class MasterDataApi {
  private readonly http = inject(HttpClient);

  /** This Samaaj's own Gautra list, resolved from the token's tenant. */
  listGautras(): Observable<Gautra[]> {
    return this.http.get<Gautra[]>('/v1/master-data/gautras', {
      params: new HttpParams().set('includeInactive', false),
    });
  }

  /** The Qualification category of the platform's flat lookup lists. */
  listQualifications(): Observable<LookupItem[]> {
    return this.http.get<LookupItem[]>('/v1/master-data/lookup-items', {
      params: new HttpParams().set('category', 'Qualification').set('includeInactive', false),
    });
  }

  /** The ProfessionType category - one of the platform's original four flat lookup lists. */
  listProfessions(): Observable<LookupItem[]> {
    return this.http.get<LookupItem[]>('/v1/master-data/lookup-items', {
      params: new HttpParams().set('category', 'ProfessionType').set('includeInactive', false),
    });
  }

  listCities(countryId?: string | null): Observable<City[]> {
    let params = new HttpParams().set('includeInactive', false);

    if (countryId) {
      params = params.set('countryId', countryId);
    }

    return this.http.get<City[]>('/v1/master-data/cities', { params });
  }

  /**
   * Scoped to one City for the address cascade, where a Locality dropdown
   * with no City chosen has nothing to show; omit `cityId` to fetch every
   * Locality on the platform, which the directory screen uses once to
   * resolve ids to names rather than scoping a search box by city.
   */
  listLocalities(cityId?: string | null): Observable<Locality[]> {
    let params = new HttpParams().set('includeInactive', false);

    if (cityId) {
      params = params.set('cityId', cityId);
    }

    return this.http.get<Locality[]>('/v1/master-data/localities', { params });
  }
}
