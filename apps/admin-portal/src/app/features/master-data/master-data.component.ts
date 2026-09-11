import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { City, Gautra, Locality, LookupCategory, LookupItem } from '../../core/admin.models';

const CATEGORIES: readonly { value: LookupCategory; label: string }[] = [
  { value: 'Country', label: 'Country' },
  { value: 'ProfessionType', label: 'Profession' },
  { value: 'Gender', label: 'Gender' },
  { value: 'MaritalStatus', label: 'Marital status' },
  { value: 'Qualification', label: 'Qualification' },
];

/**
 * Platform-wide reference data - master-data-service's own `CLAUDE.md` names
 * it as infrastructure every registration form and admin dropdown reads, and
 * until now every write to it was curl-only: the service, its validators, its
 * Outbox events and its audit descriptors shipped with no screen calling any
 * of the sixteen commands behind them.
 *
 * **Never module-gated.** A Samaaj cannot switch off the Country/Gender/Gautra
 * dropdowns its own registration form depends on (root `CLAUDE.md` §8), so
 * this screen has no "module is off" state the way most others do.
 *
 * **Two permissions split the screen in two.** `MasterData.Manage` is
 * SuperAdmin-only and covers the platform-wide lists - Country, Profession,
 * Gender, Marital status, Cities and Localities. `Gautra.Manage` is a Samaaj's
 * own list: a Samaaj Admin manages theirs, and a Super Admin manages
 * whichever Samaaj is chosen in the top bar, refused with `Gautra.NoSamaaj`
 * otherwise - the same "choose one in the top bar" shape the Pathshala create
 * form already uses, because `CreateGautraCommand` has nowhere to put a
 * Gautra with no scope selected.
 *
 * **Deactivating here has no undo.** Every write in this service is a soft
 * delete, but no service in the platform exposes a way back from it - unlike
 * a volunteer group or a Pathshala, there is no "bring back". Other services
 * hold these rows' ids as plain foreign keys, so the confirmation panel says
 * what actually happens: the row stops appearing as a choice, and it stays
 * soft-deleted.
 */
@Component({
  selector: 'app-master-data',
  imports: [FormsModule],
  template: `
    <h1 class="title">Master Data</h1>
    <p class="sub">
      Platform-wide reference data every registration form and admin dropdown reads from.
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (isSuperAdmin()) {
      <!-- Lookup lists ----------------------------------------------------- -->
      <div class="card">
        <h2>Lookup lists</h2>

        <label for="lookup-category">List</label>
        <select
          id="lookup-category"
          class="input"
          [ngModel]="category()"
          (ngModelChange)="selectCategory($event)"
        >
          @for (option of categories; track option.value) {
            <option [value]="option.value">{{ option.label }}</option>
          }
        </select>

        @if (lookupLoading()) {
          <p class="empty" role="status">Loading…</p>
        } @else if (lookupItems().length === 0) {
          <p class="empty">Nothing in this list yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">{{ categoryLabel() }} list</caption>
              <thead>
                <tr>
                  <th>Code</th><th>Label</th><th>Sort order</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (item of lookupItems(); track item.id) {
                  <tr>
                    <td>{{ item.code }}</td>
                    <td>
                      @if (editingLookupId() === item.id) {
                        <label class="sr-only" [for]="item.id + '-label'">Label</label>
                        <input
                          class="input inline"
                          [id]="item.id + '-label'"
                          [(ngModel)]="editLabel"
                          maxlength="200"
                        />
                      } @else {
                        {{ item.label }}
                      }
                    </td>
                    <td>
                      @if (editingLookupId() === item.id) {
                        <label class="sr-only" [for]="item.id + '-sort'">Sort order</label>
                        <input
                          class="input inline"
                          type="number"
                          [id]="item.id + '-sort'"
                          [(ngModel)]="editSortOrder"
                        />
                      } @else {
                        {{ item.sortOrder }}
                      }
                    </td>
                    <td>
                      <span class="pill" [class.off]="!item.isActive">
                        {{ item.isActive ? 'Active' : 'Deactivated' }}
                      </span>
                    </td>
                    <td>
                      @if (item.isActive) {
                        <div class="actions">
                          <button class="btn alt small" type="button"
                            [attr.aria-expanded]="editingLookupId() === item.id"
                            (click)="toggleEditLookup(item)">
                            Edit
                          </button>
                          <button class="btn alt small" type="button"
                            [attr.aria-expanded]="confirmingLookupId() === item.id"
                            (click)="askDeactivateLookup(item.id)">
                            Deactivate
                          </button>
                        </div>
                      }

                      @if (editingLookupId() === item.id) {
                        <div class="confirm">
                          <div class="actions">
                            <button class="btn small" type="button" [disabled]="busy()"
                              (click)="saveLookup(item)">
                              Save
                            </button>
                            <button class="btn alt small" type="button"
                              (click)="cancelEditLookup()">
                              Cancel
                            </button>
                          </div>
                        </div>
                      }

                      @if (confirmingLookupId() === item.id) {
                        <div class="confirm">
                          <p class="small" role="status">
                            {{ item.label }} stops appearing as a choice on every form that reads
                            this list. There is no undo.
                          </p>
                          <div class="actions">
                            <button class="btn small" type="button" [disabled]="busy()"
                              (click)="confirmDeactivateLookup(item)">
                              Deactivate
                            </button>
                            <button class="btn alt small" type="button"
                              (click)="confirmingLookupId.set(null)">
                              Cancel
                            </button>
                          </div>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <form class="spaced" (ngSubmit)="createLookupItem()">
          <h3>Add to {{ categoryLabel() }}</h3>

          <label for="lookup-code">Code</label>
          <input id="lookup-code" class="input" name="code" [(ngModel)]="newCode"
            maxlength="50" placeholder="IN" />

          <label for="lookup-label">Label</label>
          <input id="lookup-label" class="input" name="label" [(ngModel)]="newLabel"
            maxlength="200" placeholder="India" />

          <label for="lookup-sort">Sort order</label>
          <input id="lookup-sort" class="input" type="number" name="sortOrder"
            [(ngModel)]="newSortOrder" />

          <button class="btn" type="submit"
            [disabled]="busy() || !newCode.trim() || !newLabel.trim()">
            Add
          </button>
        </form>
      </div>

      <!-- Cities -------------------------------------------------------- -->
      <div class="card spaced">
        <h2>Cities</h2>

        <label for="city-country">Country</label>
        <select
          id="city-country"
          class="input"
          [ngModel]="selectedCountryId()"
          (ngModelChange)="onCountryChange($event)"
        >
          <option value="">Choose a country…</option>
          @for (country of countries(); track country.id) {
            <option [value]="country.id">{{ country.label }}</option>
          }
        </select>

        @if (selectedCountryId() === '') {
          <p class="empty">Choose a country to see its cities.</p>
        } @else if (citiesLoading()) {
          <p class="empty" role="status">Loading…</p>
        } @else {
          @if (cities().length === 0) {
            <p class="empty">No cities yet for this country.</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Cities in {{ selectedCountryLabel() }}</caption>
                <thead>
                  <tr><th>Name</th><th>Status</th><th></th></tr>
                </thead>
                <tbody>
                  @for (city of cities(); track city.id) {
                    <tr>
                      <td>
                        @if (editingCityId() === city.id) {
                          <label class="sr-only" [for]="city.id + '-name'">Name</label>
                          <input class="input inline" [id]="city.id + '-name'"
                            [(ngModel)]="editCityName" maxlength="200" />
                        } @else {
                          {{ city.name }}
                        }
                      </td>
                      <td>
                        <span class="pill" [class.off]="!city.isActive">
                          {{ city.isActive ? 'Active' : 'Deactivated' }}
                        </span>
                      </td>
                      <td>
                        @if (city.isActive) {
                          <div class="actions">
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="editingCityId() === city.id"
                              (click)="toggleEditCity(city)">
                              Edit
                            </button>
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="confirmingCityId() === city.id"
                              (click)="askDeactivateCity(city.id)">
                              Deactivate
                            </button>
                          </div>
                        }

                        @if (editingCityId() === city.id) {
                          <div class="confirm">
                            <div class="actions">
                              <button class="btn small" type="button" [disabled]="busy()"
                                (click)="saveCity(city)">
                                Save
                              </button>
                              <button class="btn alt small" type="button"
                                (click)="cancelEditCity()">
                                Cancel
                              </button>
                            </div>
                          </div>
                        }

                        @if (confirmingCityId() === city.id) {
                          <div class="confirm">
                            <p class="small" role="status">
                              {{ city.name }} stops appearing as a choice. There is no undo.
                            </p>
                            <div class="actions">
                              <button class="btn small" type="button" [disabled]="busy()"
                                (click)="confirmDeactivateCity(city)">
                                Deactivate
                              </button>
                              <button class="btn alt small" type="button"
                                (click)="confirmingCityId.set(null)">
                                Cancel
                              </button>
                            </div>
                          </div>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }

          <form class="spaced" (ngSubmit)="createCity()">
            <h3>Add a city to {{ selectedCountryLabel() }}</h3>

            <label for="city-name">Name</label>
            <input id="city-name" class="input" name="cityName" [(ngModel)]="newCityName"
              maxlength="200" placeholder="Udaipur" />

            <button class="btn" type="submit" [disabled]="busy() || !newCityName.trim()">
              Add city
            </button>
          </form>
        }
      </div>

      <!-- Localities ------------------------------------------------------ -->
      <div class="card spaced">
        <h2>Localities</h2>

        <label for="locality-country">Country</label>
        <select
          id="locality-country"
          class="input"
          [ngModel]="localityCountryId()"
          (ngModelChange)="onLocalityCountryChange($event)"
        >
          <option value="">Choose a country…</option>
          @for (country of countries(); track country.id) {
            <option [value]="country.id">{{ country.label }}</option>
          }
        </select>

        @if (localityCountryId() !== '') {
          <label for="locality-city">City</label>
          <select
            id="locality-city"
            class="input"
            [ngModel]="selectedCityId()"
            (ngModelChange)="onCityChange($event)"
          >
            <option value="">Choose a city…</option>
            @for (city of citiesForLocality(); track city.id) {
              <option [value]="city.id">{{ city.name }}</option>
            }
          </select>
        }

        @if (selectedCityId() === '') {
          <p class="empty">Choose a city to see its localities.</p>
        } @else if (localitiesLoading()) {
          <p class="empty" role="status">Loading…</p>
        } @else {
          @if (localities().length === 0) {
            <p class="empty">No localities yet for this city.</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Localities in {{ selectedCityLabel() }}</caption>
                <thead>
                  <tr><th>Name</th><th>Status</th><th></th></tr>
                </thead>
                <tbody>
                  @for (locality of localities(); track locality.id) {
                    <tr>
                      <td>
                        @if (editingLocalityId() === locality.id) {
                          <label class="sr-only" [for]="locality.id + '-name'">Name</label>
                          <input class="input inline" [id]="locality.id + '-name'"
                            [(ngModel)]="editLocalityName" maxlength="200" />
                        } @else {
                          {{ locality.name }}
                        }
                      </td>
                      <td>
                        <span class="pill" [class.off]="!locality.isActive">
                          {{ locality.isActive ? 'Active' : 'Deactivated' }}
                        </span>
                      </td>
                      <td>
                        @if (locality.isActive) {
                          <div class="actions">
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="editingLocalityId() === locality.id"
                              (click)="toggleEditLocality(locality)">
                              Edit
                            </button>
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="confirmingLocalityId() === locality.id"
                              (click)="askDeactivateLocality(locality.id)">
                              Deactivate
                            </button>
                          </div>
                        }

                        @if (editingLocalityId() === locality.id) {
                          <div class="confirm">
                            <div class="actions">
                              <button class="btn small" type="button" [disabled]="busy()"
                                (click)="saveLocality(locality)">
                                Save
                              </button>
                              <button class="btn alt small" type="button"
                                (click)="cancelEditLocality()">
                                Cancel
                              </button>
                            </div>
                          </div>
                        }

                        @if (confirmingLocalityId() === locality.id) {
                          <div class="confirm">
                            <p class="small" role="status">
                              {{ locality.name }} stops appearing as a choice. There is no undo.
                            </p>
                            <div class="actions">
                              <button class="btn small" type="button" [disabled]="busy()"
                                (click)="confirmDeactivateLocality(locality)">
                                Deactivate
                              </button>
                              <button class="btn alt small" type="button"
                                (click)="confirmingLocalityId.set(null)">
                                Cancel
                              </button>
                            </div>
                          </div>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }

          <form class="spaced" (ngSubmit)="createLocality()">
            <h3>Add a locality to {{ selectedCityLabel() }}</h3>

            <label for="locality-name">Name</label>
            <input id="locality-name" class="input" name="localityName"
              [(ngModel)]="newLocalityName" maxlength="200" placeholder="Hiran Magri" />

            <button class="btn" type="submit" [disabled]="busy() || !newLocalityName.trim()">
              Add locality
            </button>
          </form>
        }
      </div>
    }

    <!-- Gautra --------------------------------------------------------- -->
    @if (gautraNeedsSamaaj()) {
      <p class="notice">
        A Gautra list belongs to a Samaaj. Choose one in the top bar to manage it.
      </p>
    } @else if (canManageGautra()) {
      <div class="card spaced">
        <h2>Gautra — {{ scope.label() }}</h2>

        @if (gautraLoading()) {
          <p class="empty" role="status">Loading…</p>
        } @else if (gautras().length === 0) {
          <p class="empty">No Gautra added yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">{{ scope.label() }}'s Gautra list</caption>
              <thead>
                <tr><th>Name</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                @for (gautra of gautras(); track gautra.id) {
                  <tr>
                    <td>
                      @if (editingGautraId() === gautra.id) {
                        <label class="sr-only" [for]="gautra.id + '-name'">Name</label>
                        <input class="input inline" [id]="gautra.id + '-name'"
                          [(ngModel)]="editGautraName" maxlength="200" />
                      } @else {
                        {{ gautra.name }}
                      }
                    </td>
                    <td>
                      <span class="pill" [class.off]="!gautra.isActive">
                        {{ gautra.isActive ? 'Active' : 'Deactivated' }}
                      </span>
                    </td>
                    <td>
                      @if (gautra.isActive) {
                        <div class="actions">
                          <button class="btn alt small" type="button"
                            [attr.aria-expanded]="editingGautraId() === gautra.id"
                            (click)="toggleEditGautra(gautra)">
                            Edit
                          </button>
                          <button class="btn alt small" type="button"
                            [attr.aria-expanded]="confirmingGautraId() === gautra.id"
                            (click)="askDeactivateGautra(gautra.id)">
                            Deactivate
                          </button>
                        </div>
                      }

                      @if (editingGautraId() === gautra.id) {
                        <div class="confirm">
                          <div class="actions">
                            <button class="btn small" type="button" [disabled]="busy()"
                              (click)="saveGautra(gautra)">
                              Save
                            </button>
                            <button class="btn alt small" type="button"
                              (click)="cancelEditGautra()">
                              Cancel
                            </button>
                          </div>
                        </div>
                      }

                      @if (confirmingGautraId() === gautra.id) {
                        <div class="confirm">
                          <p class="small" role="status">
                            {{ gautra.name }} stops appearing as a choice. There is no undo.
                          </p>
                          <div class="actions">
                            <button class="btn small" type="button" [disabled]="busy()"
                              (click)="confirmDeactivateGautra(gautra)">
                              Deactivate
                            </button>
                            <button class="btn alt small" type="button"
                              (click)="confirmingGautraId.set(null)">
                              Cancel
                            </button>
                          </div>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <form class="spaced" (ngSubmit)="createGautra()">
          <h3>Add a Gautra</h3>

          <label for="gautra-name">Name</label>
          <input id="gautra-name" class="input" name="gautraName" [(ngModel)]="newGautraName"
            maxlength="200" />

          <button class="btn" type="submit" [disabled]="busy() || !newGautraName.trim()">
            Add
          </button>
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
export class MasterDataComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

  readonly categories = CATEGORIES;

  readonly isSuperAdmin = computed(() => this.auth.roles().includes('SuperAdmin'));

  /**
   * Whether this caller may manage the acting Samaaj's Gautra list.
   *
   * A Samaaj Admin's own Samaaj always qualifies. A Super Admin's does only
   * once one is chosen - `CreateGautraCommand` has nowhere to put a Gautra
   * with no scope selected, the same rule the Pathshala create form applies.
   */
  readonly canManageGautra = computed(
    () => this.auth.roles().includes('SamaajAdmin') || this.scope.tenantId() !== null,
  );

  readonly gautraNeedsSamaaj = computed(
    () => !this.auth.roles().includes('SamaajAdmin') && this.scope.tenantId() === null,
  );

  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly busy = signal(false);

  // ---- Lookup lists -------------------------------------------------------

  readonly category = signal<LookupCategory>('Country');
  readonly lookupItems = signal<readonly LookupItem[]>([]);
  readonly lookupLoading = signal(true);

  readonly categoryLabel = computed(
    () => this.categories.find((c) => c.value === this.category())?.label ?? this.category(),
  );

  readonly editingLookupId = signal<string | null>(null);
  readonly confirmingLookupId = signal<string | null>(null);
  editLabel = '';
  editSortOrder = 0;

  newCode = '';
  newLabel = '';
  newSortOrder = 0;

  // ---- Cities ---------------------------------------------------------------

  readonly countries = signal<readonly LookupItem[]>([]);
  readonly selectedCountryId = signal('');
  readonly cities = signal<readonly City[]>([]);
  readonly citiesLoading = signal(false);

  readonly selectedCountryLabel = computed(
    () => this.countries().find((c) => c.id === this.selectedCountryId())?.label ?? '',
  );

  readonly editingCityId = signal<string | null>(null);
  readonly confirmingCityId = signal<string | null>(null);
  editCityName = '';
  newCityName = '';

  // ---- Localities -----------------------------------------------------------

  readonly localityCountryId = signal('');
  readonly citiesForLocality = signal<readonly City[]>([]);
  readonly selectedCityId = signal('');
  readonly localities = signal<readonly Locality[]>([]);
  readonly localitiesLoading = signal(false);

  readonly selectedCityLabel = computed(
    () => this.citiesForLocality().find((c) => c.id === this.selectedCityId())?.name ?? '',
  );

  readonly editingLocalityId = signal<string | null>(null);
  readonly confirmingLocalityId = signal<string | null>(null);
  editLocalityName = '';
  newLocalityName = '';

  // ---- Gautra -----------------------------------------------------------

  readonly gautras = signal<readonly Gautra[]>([]);
  readonly gautraLoading = signal(true);

  readonly editingGautraId = signal<string | null>(null);
  readonly confirmingGautraId = signal<string | null>(null);
  editGautraName = '';
  newGautraName = '';

  ngOnInit(): void {
    if (this.isSuperAdmin()) {
      this.loadLookupItems();
      this.loadCountries();
    }

    if (this.canManageGautra()) {
      this.loadGautras();
    } else {
      this.gautraLoading.set(false);
    }
  }

  // ---- Lookup lists -------------------------------------------------------

  selectCategory(category: LookupCategory): void {
    this.category.set(category);
    this.editingLookupId.set(null);
    this.confirmingLookupId.set(null);
    this.loadLookupItems();
  }

  private loadLookupItems(): void {
    this.lookupLoading.set(true);

    this.api.listLookupItems(this.category(), true).subscribe({
      next: (items) => {
        this.lookupItems.set(items);
        this.lookupLoading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.lookupLoading.set(false);
      },
    });
  }

  createLookupItem(): void {
    const code = this.newCode.trim();
    const label = this.newLabel.trim();

    if (code.length === 0 || label.length === 0) {
      return;
    }

    this.act(this.api.createLookupItem(this.category(), code, label, this.newSortOrder), `${label} added.`, () => {
      this.newCode = '';
      this.newLabel = '';
      this.newSortOrder = 0;
      this.loadLookupItems();
    });
  }

  toggleEditLookup(item: LookupItem): void {
    if (this.editingLookupId() === item.id) {
      this.editingLookupId.set(null);

      return;
    }

    this.editLabel = item.label;
    this.editSortOrder = item.sortOrder;
    this.confirmingLookupId.set(null);
    this.editingLookupId.set(item.id);
  }

  cancelEditLookup(): void {
    this.editingLookupId.set(null);
  }

  saveLookup(item: LookupItem): void {
    const label = this.editLabel.trim();

    if (label.length === 0) {
      return;
    }

    this.act(this.api.updateLookupItem(item.id, label, this.editSortOrder), `${label} updated.`, () => {
      this.editingLookupId.set(null);
      this.loadLookupItems();
    });
  }

  askDeactivateLookup(id: string): void {
    if (this.confirmingLookupId() === id) {
      this.confirmingLookupId.set(null);

      return;
    }

    this.editingLookupId.set(null);
    this.confirmingLookupId.set(id);
  }

  confirmDeactivateLookup(item: LookupItem): void {
    this.act(this.api.deactivateLookupItem(item.id), `${item.label} deactivated.`, () => {
      this.confirmingLookupId.set(null);
      this.loadLookupItems();
    });
  }

  // ---- Cities ---------------------------------------------------------------

  private loadCountries(): void {
    this.api.listLookupItems('Country', false).subscribe({
      next: (items) => this.countries.set(items),
      error: () => this.countries.set([]),
    });
  }

  onCountryChange(countryId: string): void {
    this.selectedCountryId.set(countryId);
    this.editingCityId.set(null);
    this.confirmingCityId.set(null);

    if (countryId === '') {
      this.cities.set([]);

      return;
    }

    this.loadCities(countryId);
  }

  private loadCities(countryId: string): void {
    this.citiesLoading.set(true);

    this.api.listCities(countryId, true).subscribe({
      next: (found) => {
        this.cities.set(found);
        this.citiesLoading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.citiesLoading.set(false);
      },
    });
  }

  createCity(): void {
    const name = this.newCityName.trim();
    const countryId = this.selectedCountryId();

    if (name.length === 0 || countryId === '') {
      return;
    }

    this.act(this.api.createCity(countryId, name), `${name} added.`, () => {
      this.newCityName = '';
      this.loadCities(countryId);
    });
  }

  toggleEditCity(city: City): void {
    if (this.editingCityId() === city.id) {
      this.editingCityId.set(null);

      return;
    }

    this.editCityName = city.name;
    this.confirmingCityId.set(null);
    this.editingCityId.set(city.id);
  }

  cancelEditCity(): void {
    this.editingCityId.set(null);
  }

  saveCity(city: City): void {
    const name = this.editCityName.trim();

    if (name.length === 0) {
      return;
    }

    this.act(this.api.updateCity(city.id, name), `${name} updated.`, () => {
      this.editingCityId.set(null);
      this.loadCities(this.selectedCountryId());
    });
  }

  askDeactivateCity(id: string): void {
    if (this.confirmingCityId() === id) {
      this.confirmingCityId.set(null);

      return;
    }

    this.editingCityId.set(null);
    this.confirmingCityId.set(id);
  }

  confirmDeactivateCity(city: City): void {
    this.act(this.api.deactivateCity(city.id), `${city.name} deactivated.`, () => {
      this.confirmingCityId.set(null);
      this.loadCities(this.selectedCountryId());
    });
  }

  // ---- Localities -----------------------------------------------------------

  onLocalityCountryChange(countryId: string): void {
    this.localityCountryId.set(countryId);
    this.selectedCityId.set('');
    this.localities.set([]);
    this.editingLocalityId.set(null);
    this.confirmingLocalityId.set(null);

    if (countryId === '') {
      this.citiesForLocality.set([]);

      return;
    }

    this.api.listCities(countryId, false).subscribe({
      next: (found) => this.citiesForLocality.set(found),
      error: () => this.citiesForLocality.set([]),
    });
  }

  onCityChange(cityId: string): void {
    this.selectedCityId.set(cityId);
    this.editingLocalityId.set(null);
    this.confirmingLocalityId.set(null);

    if (cityId === '') {
      this.localities.set([]);

      return;
    }

    this.loadLocalities(cityId);
  }

  private loadLocalities(cityId: string): void {
    this.localitiesLoading.set(true);

    this.api.listLocalities(cityId, true).subscribe({
      next: (found) => {
        this.localities.set(found);
        this.localitiesLoading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.localitiesLoading.set(false);
      },
    });
  }

  createLocality(): void {
    const name = this.newLocalityName.trim();
    const cityId = this.selectedCityId();

    if (name.length === 0 || cityId === '') {
      return;
    }

    this.act(this.api.createLocality(cityId, name), `${name} added.`, () => {
      this.newLocalityName = '';
      this.loadLocalities(cityId);
    });
  }

  toggleEditLocality(locality: Locality): void {
    if (this.editingLocalityId() === locality.id) {
      this.editingLocalityId.set(null);

      return;
    }

    this.editLocalityName = locality.name;
    this.confirmingLocalityId.set(null);
    this.editingLocalityId.set(locality.id);
  }

  cancelEditLocality(): void {
    this.editingLocalityId.set(null);
  }

  saveLocality(locality: Locality): void {
    const name = this.editLocalityName.trim();

    if (name.length === 0) {
      return;
    }

    this.act(this.api.updateLocality(locality.id, name), `${name} updated.`, () => {
      this.editingLocalityId.set(null);
      this.loadLocalities(this.selectedCityId());
    });
  }

  askDeactivateLocality(id: string): void {
    if (this.confirmingLocalityId() === id) {
      this.confirmingLocalityId.set(null);

      return;
    }

    this.editingLocalityId.set(null);
    this.confirmingLocalityId.set(id);
  }

  confirmDeactivateLocality(locality: Locality): void {
    this.act(this.api.deactivateLocality(locality.id), `${locality.name} deactivated.`, () => {
      this.confirmingLocalityId.set(null);
      this.loadLocalities(this.selectedCityId());
    });
  }

  // ---- Gautra -----------------------------------------------------------

  private loadGautras(): void {
    this.gautraLoading.set(true);

    this.api.listGautras(true).subscribe({
      next: (found) => {
        this.gautras.set(found);
        this.gautraLoading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.gautraLoading.set(false);
      },
    });
  }

  createGautra(): void {
    const name = this.newGautraName.trim();

    if (name.length === 0) {
      return;
    }

    this.act(this.api.createGautra(name), `${name} added.`, () => {
      this.newGautraName = '';
      this.loadGautras();
    });
  }

  toggleEditGautra(gautra: Gautra): void {
    if (this.editingGautraId() === gautra.id) {
      this.editingGautraId.set(null);

      return;
    }

    this.editGautraName = gautra.name;
    this.confirmingGautraId.set(null);
    this.editingGautraId.set(gautra.id);
  }

  cancelEditGautra(): void {
    this.editingGautraId.set(null);
  }

  saveGautra(gautra: Gautra): void {
    const name = this.editGautraName.trim();

    if (name.length === 0) {
      return;
    }

    this.act(this.api.updateGautra(gautra.id, name), `${name} updated.`, () => {
      this.editingGautraId.set(null);
      this.loadGautras();
    });
  }

  askDeactivateGautra(id: string): void {
    if (this.confirmingGautraId() === id) {
      this.confirmingGautraId.set(null);

      return;
    }

    this.editingGautraId.set(null);
    this.confirmingGautraId.set(id);
  }

  confirmDeactivateGautra(gautra: Gautra): void {
    this.act(this.api.deactivateGautra(gautra.id), `${gautra.name} deactivated.`, () => {
      this.confirmingGautraId.set(null);
      this.loadGautras();
    });
  }

  // ---- Shared ---------------------------------------------------------------

  private act(work: { subscribe: (o: object) => void }, message: string, onSuccess: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        onSuccess();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
