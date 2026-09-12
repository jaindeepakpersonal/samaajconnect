import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { isNotFound } from '../../core/http-status';
import { City, Locality, LookupItem, Pathshala, State } from '../../core/admin.models';

/**
 * Jain Pathshala, from the admin wireframe's `#pathshala` screen.
 *
 * **Running a Pathshala was a curl-only activity.** Thirteen of the twenty-seven
 * endpoints `scripts/unreachable-endpoints.sh` found were this module: a parent
 * could ask for a place and nobody could open a session, create a class, place a
 * child, mark a register or record an exam. This screen and the detail beside it
 * cover the first half — setting the Pathshala up, and answering the parents.
 *
 * **Creating a Pathshala is offered to a Super Admin and to nobody else.**
 * `CreatePathshalaCommand` is Super Admin only (`DATA-MODEL.md` §9): the master
 * record is the platform operator's, and everything about *running* it is the
 * Samaaj's. This screen said for several cycles that a create form "would be a
 * control that always answers 403", which was true of a Samaaj administrator
 * and wrong about the panel — a Super Admin uses it too, scoped into a Samaaj,
 * and the endpoint was left with no caller at all as a result. The form appears
 * for the role that holds the permission, which is what the role matrix screen
 * has always done.
 *
 * **The address became a structured pick on 2026-09-12, not a free-text
 * field.** `Address` was one string; it is a `PathshalaAddress` now (a free-
 * text line plus Country, State, City and Locality ids from master-data-
 * service), the same free-text-to-master-data-pick move member-family-service
 * made for a member's own address. The cascade is Country -> State -> City ->
 * Locality because master-data-service's own `Locality` is scoped to a
 * `City`, which is scoped to a `Country` and, since the same day, optionally
 * a `State` - there is no filtering a Locality list without picking a City
 * first. Every level is optional: a Pathshala with only a free-text line and
 * nothing picked is exactly as valid as one with all four.
 */
@Component({
  selector: 'app-pathshala-list',
  imports: [FormsModule, RouterLink],
  template: `
    <h1 class="title">Jain Pathshala</h1>
    <p class="sub">Sessions, classes and the children waiting for a place in {{ scope.label() }}.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">A Pathshala belongs to a Samaaj. Choose one in the top bar.</p>
    } @else if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the Pathshala module. Switch it on from the Samaaj
        screen.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (pathshalas().length === 0 && !canCreate()) {
      <p class="empty">
        This Samaaj has no Pathshala yet. Only the platform operator can create the master
        record; ask them to add one.
      </p>
    } @else {
      <div class="grid">
        @for (item of pathshalas(); track item.id) {
          <div class="card">
            <h2>{{ item.name }}</h2>

            @if (addressSummary(item.address); as summary) {
              <p class="muted">{{ summary }}</p>
            }

            <p>
              @if (item.currentSessionLabel) {
                <span class="pill ok">{{ item.currentSessionLabel }}</span>
              } @else {
                <span class="pill warn">No session open</span>
              }
              @if (!item.acceptsEnrolments) {
                <span class="pill off">Not taking enrolments</span>
              }
            </p>

            <!--
              Counts, not rosters. The service answers with three numbers it can
              produce without sending anybody a list of children.
            -->
            <p class="muted">
              {{ item.classCount }} {{ item.classCount === 1 ? 'class' : 'classes' }} ·
              {{ item.teacherCount }} {{ item.teacherCount === 1 ? 'teacher' : 'teachers' }}
            </p>

            <div class="actions">
              <a class="btn" [routerLink]="['/pathshala', item.id]">Open</a>
            </div>
          </div>
        }
      </div>

      @if (canCreate()) {
        <div class="card spaced">
          <h2>Create a Pathshala</h2>
          <p class="small">
            The master record belongs to the platform, which is why this is here and not on a
            Samaaj administrator's screen. Everything about running it — sessions, classes,
            placing children — is theirs.
          </p>

          @if (done(); as message) {
            <p class="notice ok" role="status">{{ message }}</p>
          }

          <form (ngSubmit)="create()">
            <label for="pathshala-name">Name</label>
            <input id="pathshala-name" class="input" name="name" [(ngModel)]="name"
              maxlength="200" placeholder="Shri Mahavir Jain Pathshala" />

            <label for="pathshala-address">Address line</label>
            <input id="pathshala-address" class="input" name="address" [(ngModel)]="addressLine"
              maxlength="500" placeholder="Building, street" />

            <label for="pathshala-country">Country</label>
            <select id="pathshala-country" class="input" name="country"
              [ngModel]="countryId()" (ngModelChange)="onCountryChange($event)">
              <option value="">Not specified</option>
              @for (country of countries(); track country.id) {
                <option [value]="country.id">{{ country.label }}</option>
              }
            </select>

            @if (countryId() !== '') {
              <label for="pathshala-state">State</label>
              <select id="pathshala-state" class="input" name="state"
                [ngModel]="stateId()" (ngModelChange)="onStateChange($event)">
                <option value="">Not specified</option>
                @for (state of states(); track state.id) {
                  <option [value]="state.id">{{ state.name }}</option>
                }
              </select>

              <label for="pathshala-city">City</label>
              <select id="pathshala-city" class="input" name="city"
                [ngModel]="cityId()" (ngModelChange)="onCityChange($event)">
                <option value="">Not specified</option>
                @for (city of cities(); track city.id) {
                  <option [value]="city.id">{{ city.name }}</option>
                }
              </select>
            }

            @if (cityId() !== '') {
              <label for="pathshala-locality">Locality</label>
              <select id="pathshala-locality" class="input" name="locality" [(ngModel)]="localityId">
                <option value="">Not specified</option>
                @for (locality of localities(); track locality.id) {
                  <option [value]="locality.id">{{ locality.name }}</option>
                }
              </select>
            }

            <label for="pathshala-contact">Contact person</label>
            <input id="pathshala-contact" class="input" name="contactPerson"
              [(ngModel)]="contactPerson" maxlength="200" />

            <button class="btn" type="submit" [disabled]="busy() || !name.trim()">
              Create for {{ scope.label() }}
            </button>
          </form>
        </div>
      }
    }
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: var(--space-4);
    }

    .spaced {
      margin-top: var(--space-4);
    }
  `,
})
export class PathshalaListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

  readonly pathshalas = signal<readonly Pathshala[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  name = '';
  addressLine = '';
  contactPerson = '';

  // ---- Address: Country -> State -> City -> Locality -----------------------

  readonly countries = signal<readonly LookupItem[]>([]);
  readonly countryId = signal('');
  readonly states = signal<readonly State[]>([]);
  readonly stateId = signal('');
  readonly cities = signal<readonly City[]>([]);
  readonly cityId = signal('');
  readonly localities = signal<readonly Locality[]>([]);
  localityId = '';

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  /**
   * Whether to offer the create form.
   *
   * The role *and* a chosen Samaaj: the command creates the record inside
   * whichever Samaaj the request is scoped to, so offering it with no scope
   * would be offering to create one nowhere in particular.
   */
  readonly canCreate = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() !== null,
  );

  ngOnInit(): void {
    this.load();
    this.api.listLookupItems('Country', false).subscribe({
      next: (found) => this.countries.set(found),
      error: () => this.countries.set([]),
    });
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

  /**
   * The free-text line only. Country/State/City/Locality are ids resolved
   * against whichever ones are loaded for the create form's own current
   * selection, not against every Pathshala's own address - resolving each
   * card's own city and locality name would mean a lookup per Pathshala on
   * a screen that lists all of them, the same cost this app avoids
   * everywhere else it shows an id rather than inventing a name for one it
   * cannot resolve cheaply.
   */
  addressSummary(address: Pathshala['address']): string | null {
    return address?.line ?? null;
  }

  create(): void {
    const name = this.name.trim();

    if (name.length === 0) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api
      .createPathshala(
        name,
        {
          addressLine: blankToNull(this.addressLine),
          countryId: this.countryId() || null,
          stateId: this.stateId() || null,
          cityId: this.cityId() || null,
          localityId: this.localityId || null,
        },
        blankToNull(this.contactPerson),
      )
      .subscribe({
        next: () => {
          this.done.set(`${name} created. Open a session to start teaching.`);
          this.busy.set(false);
          this.name = '';
          this.addressLine = '';
          this.countryId.set('');
          this.stateId.set('');
          this.cityId.set('');
          this.localityId = '';
          this.states.set([]);
          this.cities.set([]);
          this.localities.set([]);
          this.contactPerson = '';
          this.load();
        },
        error: (failure: unknown) => {
          this.error.set(describeError(failure));
          this.busy.set(false);
        },
      });
  }

  private load(): void {
    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.api.listPathshalas().subscribe({
      next: (found) => {
        this.pathshalas.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        // 404 here is the module gate, not a missing route: the gateway answers
        // 404 for a Samaaj that has switched `pathshala` off, so that a Samaaj
        // without the module is indistinguishable from a platform with no such
        // feature.
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
