import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { City, Locality, LookupItem, State, Trust, TrustStatus } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * A Samaaj's local Trusts — one per locality — and setting a new one up.
 *
 * No wireframe covers this: it is a new module, with no wireframe equivalent.
 *
 * **Exactly one Trust per locality, enforced by the service, not this
 * screen.** `CreateTrustCommand` refuses a second Trust on a locality
 * already claimed with a 409 - this form does not try to pre-check that, it
 * just shows the service's own refusal.
 *
 * **The locality picker is the Pathshala create form's own Country -> State
 * -> City -> Locality cascade**, copied rather than reinvented, with one
 * difference: a Pathshala's address is optional, a Trust's locality is not,
 * so the create button stays disabled until one is actually chosen.
 *
 * **Membership, positions and elections live on the detail screen.** This
 * one is the directory and the on/off switch.
 */
@Component({
  selector: 'app-trusts-list',
  imports: [FormsModule, RouterLink],
  template: `
    <h1 class="title">Trusts</h1>
    <p class="sub">
      A Samaaj's local Trust, one per locality. Set one up, and manage its roster and
      elections from its own page.
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the Trust module. Switch it on under the Samaaj's
        settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <h2>Trusts</h2>

        @if (trusts().length === 0) {
          <p class="empty">None yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">This Samaaj's Trusts</caption>
              <thead>
                <tr><th>Name</th><th>Locality</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                @for (trust of trusts(); track trust.id) {
                  <tr>
                    <td><a [routerLink]="['/trust', trust.id]"><b>{{ trust.name }}</b></a></td>
                    <td>{{ localityName(trust.localityId) }}</td>
                    <td>
                      <span class="pill" [class.warn]="trust.status === 'Inactive'">
                        {{ trust.status }}
                      </span>
                    </td>
                    <td>
                      @if (trust.status === 'Active') {
                        <button class="btn small alt" type="button" [disabled]="busy()"
                          (click)="setStatus(trust, 'Inactive')">
                          Deactivate
                        </button>
                      } @else {
                        <button class="btn small" type="button" [disabled]="busy()"
                          (click)="setStatus(trust, 'Active')">
                          Reactivate
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="card spaced">
        <h2>Set up a Trust</h2>

        <form (ngSubmit)="create()">
          <div class="form-grid">
            <div class="field">
              <label for="trust-name">Name</label>
              <input id="trust-name" class="input" name="name" [(ngModel)]="name"
                maxlength="200" placeholder="Hiran Magri Samaaj Trust" />
            </div>

            <div class="field">
              <label for="trust-country">Country</label>
              <select id="trust-country" class="input" name="country"
                [ngModel]="countryId()" (ngModelChange)="onCountryChange($event)">
                <option value="">Choose…</option>
                @for (country of countries(); track country.id) {
                  <option [value]="country.id">{{ country.label }}</option>
                }
              </select>
            </div>

            @if (countryId() !== '') {
              <div class="field">
                <label for="trust-state">State</label>
                <select id="trust-state" class="input" name="state"
                  [ngModel]="stateId()" (ngModelChange)="onStateChange($event)">
                  <option value="">Not specified</option>
                  @for (state of states(); track state.id) {
                    <option [value]="state.id">{{ state.name }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="trust-city">City/Village</label>
                <select id="trust-city" class="input" name="city"
                  [ngModel]="cityId()" (ngModelChange)="onCityChange($event)">
                  <option value="">Choose…</option>
                  @for (city of cities(); track city.id) {
                    <option [value]="city.id">{{ city.name }}</option>
                  }
                </select>
              </div>
            }

            @if (cityId() !== '') {
              <div class="field">
                <label for="trust-locality">Locality</label>
                <select id="trust-locality" class="input" name="locality" [(ngModel)]="localityId">
                  <option value="">Choose…</option>
                  @for (locality of localities(); track locality.id) {
                    <option [value]="locality.id">{{ locality.name }}</option>
                  }
                </select>
              </div>
            }

            <p class="small span-all">
              A locality can hold exactly one Trust. Creating a second one for the same
              locality is refused.
            </p>

            <div class="span-all">
              <button class="btn" type="submit" [disabled]="busy() || !canCreate()">
                Create Trust
              </button>
            </div>
          </div>
        </form>
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }
  `,
})
export class TrustsListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly trusts = signal<readonly Trust[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  name = '';

  // ---- Locality: Country -> State -> City -> Locality ----------------------

  readonly countries = signal<readonly LookupItem[]>([]);
  readonly countryId = signal('');
  readonly states = signal<readonly State[]>([]);
  readonly stateId = signal('');
  readonly cities = signal<readonly City[]>([]);
  readonly cityId = signal('');
  readonly localities = signal<readonly Locality[]>([]);
  localityId = '';

  private readonly localityNames = computed(
    () => new Map(this.localities().map((l) => [l.id, l.name])),
  );

  ngOnInit(): void {
    this.load();
    this.api.listLookupItems('Country', false).subscribe({
      next: (found) => this.countries.set(found),
      error: () => this.countries.set([]),
    });
  }

  canCreate(): boolean {
    return this.name.trim().length > 0 && this.localityId.length > 0;
  }

  /**
   * A best-effort label. This screen only ever holds the localities under
   * whichever city was last chosen in the create form, so an existing
   * Trust's own locality falls back to its raw id rather than an incorrect
   * name from a different city's list.
   */
  localityName(localityId: string): string {
    return this.localityNames().get(localityId) ?? localityId;
  }

  onCountryChange(countryId: string): void {
    this.countryId.set(countryId);
    this.stateId.set('');
    this.cityId.set('');
    this.localityId = '';
    this.states.set([]);
    this.cities.set([]);
    this.localities.set([]);

    if (countryId === '') {
      return;
    }

    this.api.listStates(countryId).subscribe({
      next: (found) => this.states.set(found),
      error: () => this.states.set([]),
    });
    this.api.listCities(countryId).subscribe({
      next: (found) => this.cities.set(found),
      error: () => this.cities.set([]),
    });
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

    if (cityId === '') {
      return;
    }

    this.api.listLocalities(cityId).subscribe({
      next: (found) => this.localities.set(found),
      error: () => this.localities.set([]),
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listTrusts().subscribe({
      next: (found) => {
        this.trusts.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    const name = this.name.trim();
    const localityId = this.localityId;

    this.act(this.api.createTrust(localityId, name), `${name} created.`, () => {
      this.name = '';
      this.localityId = '';
    });
  }

  setStatus(trust: Trust, status: TrustStatus): void {
    this.act(
      this.api.changeTrustStatus(trust.id, status),
      status === 'Inactive' ? `${trust.name} is deactivated.` : `${trust.name} is active again.`,
    );
  }

  private act(work: { subscribe: (o: object) => void }, message: string, reset?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        reset?.();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
