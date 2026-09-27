import { Component, OnInit, inject, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocalityOptions } from './locality-options';

/** Every row, whatever its locality. */
export const ALL_LOCALITIES = '';

/** Only rows for the whole Samaaj - no locality of their own. */
export const WHOLE_SAMAAJ = 'whole-samaaj';

/**
 * Whether a row belongs under the filter: everything for "All", rows with no
 * locality for "Whole Samaaj", and otherwise that one locality.
 */
export function matchesLocality(filter: string, localityId: string | null | undefined): boolean {
  if (filter === ALL_LOCALITIES) {
    return true;
  }

  if (filter === WHOLE_SAMAAJ) {
    return !localityId;
  }

  return localityId === filter;
}

/**
 * The locality filter every module list carries - one select, from the list
 * `LocalityOptions` loads once per session, so every screen offers the same
 * localities under the same names.
 *
 * **It narrows what the screen already has.** Each of these lists is small
 * and loaded whole, and every row names its locality, so filtering on the page
 * costs no request and cannot disagree with the rows it filters. The one
 * server-side filter is Family &amp; Children, whose list is paged and whose
 * locality is its members' addresses rather than a field on the row.
 */
@Component({
  selector: 'app-locality-filter',
  imports: [FormsModule],
  template: `
    <div class="locality-filter">
      <label [for]="inputId()">Locality</label>
      <select class="input inline" [id]="inputId()" [name]="inputId()" [(ngModel)]="value"
        [disabled]="disabled()">
        @if (offerAll()) {
          <option [value]="all">All localities</option>
          @if (offerWholeSamaaj()) {
            <option [value]="wholeSamaaj">The whole Samaaj only</option>
          }
        }
        @for (option of localities.options(); track option.id) {
          <option [value]="option.id">{{ option.label }}</option>
        }
      </select>
    </div>
  `,
  styles: `
    .locality-filter {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin: var(--space-2) 0 var(--space-3);
    }

    .locality-filter label {
      margin: 0;
    }
  `,
})
export class LocalityFilterComponent implements OnInit {
  readonly localities = inject(LocalityOptions);

  /** The chosen filter: `ALL_LOCALITIES`, `WHOLE_SAMAAJ` or a locality id. */
  readonly value = model<string>(ALL_LOCALITIES);

  readonly inputId = input('locality-filter');

  /** False where the list is only ever one locality's, as for an Area Admin. */
  readonly offerAll = input(true);

  /** False where a row has no locality of its own to be missing - a household's is its members'. */
  readonly offerWholeSamaaj = input(true);
  readonly disabled = input(false);

  readonly all = ALL_LOCALITIES;
  readonly wholeSamaaj = WHOLE_SAMAAJ;

  ngOnInit(): void {
    this.localities.load();
  }
}
