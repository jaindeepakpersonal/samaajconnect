import { Injectable, computed, inject } from '@angular/core';
import { AuthService } from '@samaajconnect/shared';
import { City, Locality } from './admin.models';

/**
 * The Area Admin's one locality, for the screens that have to decide which
 * controls to offer.
 *
 * A courtesy, never the control: each service applies the same locality from
 * the token and answers 404 outside it. This only keeps the panel from drawing
 * buttons that would.
 */
@Injectable({ providedIn: 'root' })
export class AreaScope {
  private readonly auth = inject(AuthService);

  /** The Area Admin's locality; null for everyone else. */
  readonly localityId = computed(() => this.auth.user()?.areaLocalityId ?? null);

  /** Whether this caller administers something in that locality. */
  administers(localityId: string | null | undefined): boolean {
    const own = this.localityId();

    return own === null || localityId === own;
  }
}

/**
 * Localities named with their city, sorted - locality names repeat across
 * cities, and a picker showing three "Hiran Magri" rows leaves an
 * administrator guessing which one they meant.
 */
export function labelLocalities(
  localities: readonly Locality[],
  cities: readonly City[],
): { id: string; label: string }[] {
  const cityNames = new Map(cities.map((c) => [c.id, c.name]));

  return localities
    .map((l) => {
      const city = cityNames.get(l.cityId);
      return { id: l.id, label: city ? `${l.name}, ${city}` : l.name };
    })
    .sort((a, b) => a.label.localeCompare(b.label));
}
