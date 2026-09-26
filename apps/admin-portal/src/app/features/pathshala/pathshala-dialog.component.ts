import {
  AfterViewInit,
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AreaScope } from '../../core/area-scope';
import { City, Locality, LookupItem, Pathshala, State } from '../../core/admin.models';

/**
 * Creating or editing a Pathshala, as a modal over the list - the same dialog
 * shape Events and Volunteer Groups use (`.modal-backdrop`/`.modal-dialog`,
 * focus kept inside, Escape closes, focus returned to whatever opened it).
 *
 * **One form for both, because they are the same fields.** Name, address
 * (a free-text line plus the Country -> State -> City -> Locality cascade,
 * each level loaded only once its parent is chosen) and contact person.
 * Creating is a Super Admin's act and editing is anybody's who runs the
 * Pathshala; the list decides which to offer, and the service re-checks both.
 *
 * **Editing loads every level of the cascade above a set one before it is
 * shown**, because a select whose options have not arrived renders blank, which
 * would read as the address being empty. **An Area Admin sees the cascade
 * locked**: the service will not move a Pathshala out of their locality.
 */
@Component({
  selector: 'app-pathshala-dialog',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="closed.emit()">
      <div
        class="card modal-card modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="pathshala-dialog-title"
        (click)="$event.stopPropagation()"
        (keydown)="onKeydown($event)"
      >
        <h2 id="pathshala-dialog-title">
          {{ pathshala() ? 'Edit ' + pathshala()!.name : 'Create a Pathshala' }}
        </h2>

        @if (!pathshala()) {
          <p class="small">
            The master record belongs to the platform, which is why only a Super Admin creates one.
            Everything about running it - sessions, classes, placing children - is the Samaaj's.
          </p>
        }

        @if (error(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        <form (ngSubmit)="save()">
          <label for="pathshala-name">Name</label>
          <input id="pathshala-name" class="input" name="name" [(ngModel)]="name"
            maxlength="200" placeholder="Shri Mahavir Jain Pathshala" #firstField />

          <label for="pathshala-address">Address line</label>
          <input id="pathshala-address" class="input" name="address" [(ngModel)]="addressLine"
            maxlength="500" placeholder="Building, street" />

          @if (area.localityId()) {
            <p class="small">
              The locality stays yours: moving a Pathshala to another is its Samaaj Admin's decision.
            </p>
          }

          <label for="pathshala-country">Country</label>
          <select id="pathshala-country" class="input" name="country" [disabled]="!!area.localityId()"
            [ngModel]="countryId()" (ngModelChange)="onCountryChange($event)">
            <option value="">Not specified</option>
            @for (country of countries(); track country.id) {
              <option [value]="country.id">{{ country.label }}</option>
            }
          </select>

          @if (countryId() !== '') {
            <label for="pathshala-state">State</label>
            <select id="pathshala-state" class="input" name="state" [disabled]="!!area.localityId()"
              [ngModel]="stateId()" (ngModelChange)="onStateChange($event)">
              <option value="">Not specified</option>
              @for (state of states(); track state.id) {
                <option [value]="state.id">{{ state.name }}</option>
              }
            </select>

            <label for="pathshala-city">City/Village</label>
            <select id="pathshala-city" class="input" name="city" [disabled]="!!area.localityId()"
              [ngModel]="cityId()" (ngModelChange)="onCityChange($event)">
              <option value="">Not specified</option>
              @for (city of cities(); track city.id) {
                <option [value]="city.id">{{ city.name }}</option>
              }
            </select>
          }

          @if (cityId() !== '') {
            <label for="pathshala-locality">Locality</label>
            <select id="pathshala-locality" class="input" name="locality" [disabled]="!!area.localityId()"
              [(ngModel)]="localityId">
              <option value="">Not specified</option>
              @for (locality of localities(); track locality.id) {
                <option [value]="locality.id">{{ locality.name }}</option>
              }
            </select>
            <p class="small">
              The locality decides which Area Admin runs this Pathshala. With none, only the Samaaj
              Admin does.
            </p>
          }

          <label for="pathshala-contact">Contact person</label>
          <input id="pathshala-contact" class="input" name="contactPerson"
            [(ngModel)]="contactPerson" maxlength="200" />

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy() || !name.trim()">
              {{ pathshala() ? 'Save' : 'Create for ' + samaajLabel() }}
            </button>
            <button class="btn alt" type="button" (click)="closed.emit()">Cancel</button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class PathshalaDialogComponent implements OnInit, AfterViewInit {
  private readonly api = inject(AdminApi);

  readonly area = inject(AreaScope);

  /** The Pathshala being edited; absent to create one. */
  readonly pathshala = input<Pathshala | null>(null);

  /** The Samaaj being created into, for the button. */
  readonly samaajLabel = input('this Samaaj');

  /** Emitted with a sentence saying what happened. */
  readonly saved = output<string>();
  readonly closed = output<void>();

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  name = '';
  addressLine = '';
  contactPerson = '';

  readonly countries = signal<readonly LookupItem[]>([]);
  readonly countryId = signal('');
  readonly states = signal<readonly State[]>([]);
  readonly stateId = signal('');
  readonly cities = signal<readonly City[]>([]);
  readonly cityId = signal('');
  readonly localities = signal<readonly Locality[]>([]);
  localityId = '';

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  ngOnInit(): void {
    const existing = this.pathshala();

    if (existing !== null) {
      const address = existing.address;

      this.name = existing.name;
      this.addressLine = address?.line ?? '';
      this.contactPerson = existing.contactPerson ?? '';
      this.countryId.set(address?.countryId ?? '');
      this.stateId.set(address?.stateId ?? '');
      this.cityId.set(address?.cityId ?? '');
      this.localityId = address?.localityId ?? '';
    }

    this.api.listLookupItems('Country', false).subscribe({
      next: (found) => this.countries.set(found),
      error: () => this.countries.set([]),
    });
    this.loadStatesAndCities();
    this.loadLocalities();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  onCountryChange(countryId: string): void {
    this.countryId.set(countryId);
    this.stateId.set('');
    this.cityId.set('');
    this.localityId = '';
    this.states.set([]);
    this.cities.set([]);
    this.localities.set([]);
    this.loadStatesAndCities();
  }

  onStateChange(stateId: string): void {
    this.stateId.set(stateId);
    this.cityId.set('');
    this.localityId = '';
    this.localities.set([]);

    this.api.listCities(this.countryId(), stateId || null).subscribe({
      next: (found) => this.cities.set(found),
      error: () => this.cities.set([]),
    });
  }

  onCityChange(cityId: string): void {
    this.cityId.set(cityId);
    this.localityId = '';
    this.localities.set([]);
    this.loadLocalities();
  }

  save(): void {
    const name = this.name.trim();

    if (name.length === 0 || this.busy()) {
      return;
    }

    const address = {
      addressLine: blankToNull(this.addressLine),
      countryId: this.countryId() || null,
      stateId: this.stateId() || null,
      cityId: this.cityId() || null,
      localityId: this.localityId || null,
    };
    const contact = blankToNull(this.contactPerson);
    const existing = this.pathshala();

    this.busy.set(true);
    this.error.set(null);

    const work =
      existing === null
        ? this.api.createPathshala(name, address, contact)
        : this.api.updatePathshala(existing.id, name, address, contact);

    work.subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.emit(
          existing === null ? `${name} created. Open it to start a session.` : `${name} is saved.`,
        );
      },
      error: (failure: unknown) => {
        // Kept inside the dialog with the form open, so a refusal does not
        // cost what was typed.
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closed.emit();

      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const dialog = event.currentTarget as HTMLElement;
    const focusable = dialog.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    );

    if (focusable.length === 0) {
      return;
    }

    const first = focusable[0]!;
    const last = focusable[focusable.length - 1]!;

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private loadStatesAndCities(): void {
    const countryId = this.countryId();

    if (countryId === '') {
      return;
    }

    this.api.listStates(countryId).subscribe({
      next: (found) => this.states.set(found),
      error: () => this.states.set([]),
    });
    this.api.listCities(countryId, this.stateId() || null).subscribe({
      next: (found) => this.cities.set(found),
      error: () => this.cities.set([]),
    });
  }

  private loadLocalities(): void {
    const cityId = this.cityId();

    if (cityId === '') {
      return;
    }

    this.api.listLocalities(cityId).subscribe({
      next: (found) => this.localities.set(found),
      error: () => this.localities.set([]),
    });
  }
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
