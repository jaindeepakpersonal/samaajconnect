import { Injectable, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { AdminApi } from './admin-api';
import { labelLocalities } from './area-scope';

export interface LocalityOption {
  readonly id: string;
  readonly label: string;
}

/**
 * Every locality on the platform as "Name, City", sorted - loaded once per
 * session and shared by every screen that picks one or filters by one.
 *
 * Several modules carry an optional locality (events, groups, occasions,
 * competitions, Trusts, Pathshalas, issues), and each used to load the list
 * for itself. Seven copies of one request is the same list fetched seven
 * times, and seven chances for a label to be worded differently.
 *
 * A failure leaves the list empty rather than failing the screen: a locality
 * is a courtesy on a directory row, and a form still offers "Whole Samaaj".
 */
@Injectable({ providedIn: 'root' })
export class LocalityOptions {
  private readonly api = inject(AdminApi);
  private readonly loaded = signal<readonly LocalityOption[] | null>(null);
  private readonly cityNames = signal<ReadonlyMap<string, string>>(new Map());
  private requested = false;

  readonly options = computed(() => this.loaded() ?? []);

  /** Asks for the list the first time anybody needs it. Safe to call on every init. */
  load(): void {
    if (this.requested) {
      return;
    }

    this.requested = true;

    forkJoin({ localities: this.api.listLocalities(), cities: this.api.listCities() }).subscribe({
      next: ({ localities, cities }) => {
        this.cityNames.set(new Map(cities.map((c) => [c.id, c.name])));
        this.loaded.set(labelLocalities(localities, cities));
      },
      error: () => {
        // Asked again next time rather than stuck empty for the session.
        this.requested = false;
        this.loaded.set([]);
      },
    });
  }

  /**
   * Where an address is, as "Locality, City/Village" - or the city alone when
   * no locality is picked, and null when neither is (or neither has loaded).
   * Never a raw id.
   */
  place(localityId: string | null | undefined, cityId: string | null | undefined): string | null {
    if (localityId) {
      const found = this.options().find((o) => o.id === localityId);

      if (found) {
        return found.label;
      }
    }

    return cityId ? (this.cityNames().get(cityId) ?? null) : null;
  }

  /** "Hiran Magri, Udaipur", "Whole Samaaj" for none, and never a raw id. */
  label(localityId: string | null | undefined): string {
    if (!localityId) {
      return 'Whole Samaaj';
    }

    return this.options().find((o) => o.id === localityId)?.label ?? 'A locality';
  }
}
