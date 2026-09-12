import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { City, Gautra, Locality, LookupCategory, LookupItem } from '../../core/admin.models';

type TabKey = LookupCategory | 'Cities' | 'Localities' | 'Gautra';

const LOOKUP_TABS: readonly { value: LookupCategory; label: string }[] = [
  { value: 'Country', label: 'Country' },
  { value: 'ProfessionType', label: 'Profession' },
  { value: 'Gender', label: 'Gender' },
  { value: 'MaritalStatus', label: 'Marital status' },
  { value: 'Qualification', label: 'Qualification' },
];

const ALL_TABS: readonly { value: TabKey; label: string }[] = [
  ...LOOKUP_TABS,
  { value: 'Cities', label: 'Cities' },
  { value: 'Localities', label: 'Localities' },
  { value: 'Gautra', label: 'Gautra' },
];

/**
 * Platform-wide reference data - master-data-service's own `CLAUDE.md` names
 * it as infrastructure every registration form and admin dropdown reads, and
 * until now every write to it was curl-only: the service, its validators, its
 * Outbox events and its audit descriptors shipped with no screen calling any
 * of the sixteen commands behind them.
 *
 * **One type of master data at a time, chosen from a horizontal tab bar.**
 * The eight tabs - the five `LookupCategory` values, Cities, Localities and
 * Gautra - used to be four cards stacked and all visible together, with the
 * five lookup categories sharing one of those cards behind a `<select>`.
 * Redesigned 2026-09-12 into one tab per data type: a SuperAdmin managing
 * Marital status no longer scrolls past Countries, Cities and Localities to
 * reach it, and switching data types is a tab click rather than a dropdown
 * change buried inside a card.
 *
 * **Add and Edit are a modal, not an always-open inline form or an
 * in-row-swap.** The inline form sat under every list whether or not anyone
 * was about to use it, and editing swapped a table row's cells for inputs in
 * place - both readable once you knew the screen, neither obvious to a first
 * visit. A modal makes "add" and "edit" a deliberate, focused action with
 * nothing else competing for attention, and returns focus to the button that
 * opened it on close - the same "don't drop keyboard focus" rule this app's
 * own accessibility notes already hold every confirmation panel to.
 * Deactivate stays an inline confirmation: it was never the part of this
 * screen that was hard to find, only add and edit were.
 *
 * **Never module-gated.** A Samaaj cannot switch off the Country/Gender/Gautra
 * dropdowns its own registration form depends on (root `CLAUDE.md` §8), so
 * this screen has no "module is off" state the way most others do.
 *
 * **Two permissions split the tabs in two.** `MasterData.Manage` is
 * SuperAdmin-only and covers the seven platform-wide tabs - the five lookup
 * categories, Cities and Localities. `Gautra.Manage` is a Samaaj's own list: a
 * Samaaj Admin manages theirs, and a Super Admin manages whichever Samaaj is
 * chosen in the top bar, refused with `Gautra.NoSamaaj` otherwise - the same
 * "choose one in the top bar" shape the Pathshala create form already uses,
 * because `CreateGautraCommand` has nowhere to put a Gautra with no scope
 * selected. The Gautra tab is always shown; its content is either the
 * managed list or the "choose a Samaaj" notice.
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

    <div class="tabs" role="tablist" aria-label="Master data type">
      @for (tab of visibleTabs(); track tab.value) {
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === tab.value"
          [attr.aria-selected]="activeTab() === tab.value"
          (click)="selectTab(tab.value)"
        >
          {{ tab.label }}
        </button>
      }
    </div>

    <!-- Lookup lists: Country, Profession, Gender, Marital status, Qualification -->
    @if (isLookupTab(activeTab())) {
      <div class="card">
        <div class="section-head">
          <h2>{{ activeTabLabel() }}</h2>
          <button class="btn small" type="button" (click)="openAddLookup($event)">Add</button>
        </div>

        @if (lookupLoading()) {
          <p class="empty" role="status">Loading…</p>
        } @else if (lookupItems().length === 0) {
          <p class="empty">Nothing in this list yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">{{ activeTabLabel() }} list</caption>
              <thead>
                <tr>
                  <th>Code</th><th>Label</th><th>Sort order</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (item of lookupItems(); track item.id) {
                  <tr>
                    <td>{{ item.code }}</td>
                    <td>{{ item.label }}</td>
                    <td>{{ item.sortOrder }}</td>
                    <td>
                      <span class="pill" [class.off]="!item.isActive">
                        {{ item.isActive ? 'Active' : 'Deactivated' }}
                      </span>
                    </td>
                    <td>
                      @if (item.isActive) {
                        <div class="actions">
                          <button class="btn alt small" type="button" (click)="openEditLookup(item, $event)">
                            Edit
                          </button>
                          <button class="btn alt small" type="button"
                            [attr.aria-expanded]="confirmingLookupId() === item.id"
                            (click)="askDeactivateLookup(item.id)">
                            Deactivate
                          </button>
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
      </div>
    }

    <!-- Cities -------------------------------------------------------- -->
    @if (activeTab() === 'Cities') {
      <div class="card">
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
        } @else {
          <div class="section-head">
            <h2>Cities in {{ selectedCountryLabel() }}</h2>
            <button class="btn small" type="button" (click)="openAddCity($event)">Add city</button>
          </div>

          @if (citiesLoading()) {
            <p class="empty" role="status">Loading…</p>
          } @else if (cities().length === 0) {
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
                      <td>{{ city.name }}</td>
                      <td>
                        <span class="pill" [class.off]="!city.isActive">
                          {{ city.isActive ? 'Active' : 'Deactivated' }}
                        </span>
                      </td>
                      <td>
                        @if (city.isActive) {
                          <div class="actions">
                            <button class="btn alt small" type="button" (click)="openEditCity(city, $event)">
                              Edit
                            </button>
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="confirmingCityId() === city.id"
                              (click)="askDeactivateCity(city.id)">
                              Deactivate
                            </button>
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
        }
      </div>
    }

    <!-- Localities ------------------------------------------------------ -->
    @if (activeTab() === 'Localities') {
      <div class="card">
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
        } @else {
          <div class="section-head">
            <h2>Localities in {{ selectedCityLabel() }}</h2>
            <button class="btn small" type="button" (click)="openAddLocality($event)">
              Add locality
            </button>
          </div>

          @if (localitiesLoading()) {
            <p class="empty" role="status">Loading…</p>
          } @else if (localities().length === 0) {
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
                      <td>{{ locality.name }}</td>
                      <td>
                        <span class="pill" [class.off]="!locality.isActive">
                          {{ locality.isActive ? 'Active' : 'Deactivated' }}
                        </span>
                      </td>
                      <td>
                        @if (locality.isActive) {
                          <div class="actions">
                            <button class="btn alt small" type="button"
                              (click)="openEditLocality(locality, $event)">
                              Edit
                            </button>
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="confirmingLocalityId() === locality.id"
                              (click)="askDeactivateLocality(locality.id)">
                              Deactivate
                            </button>
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
        }
      </div>
    }

    <!-- Gautra --------------------------------------------------------- -->
    @if (activeTab() === 'Gautra') {
      @if (gautraNeedsSamaaj()) {
        <p class="notice">
          A Gautra list belongs to a Samaaj. Choose one in the top bar to manage it.
        </p>
      } @else if (canManageGautra()) {
        <div class="card">
          <div class="section-head">
            <h2>Gautra — {{ scope.label() }}</h2>
            <button class="btn small" type="button" (click)="openAddGautra($event)">Add</button>
          </div>

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
                      <td>{{ gautra.name }}</td>
                      <td>
                        <span class="pill" [class.off]="!gautra.isActive">
                          {{ gautra.isActive ? 'Active' : 'Deactivated' }}
                        </span>
                      </td>
                      <td>
                        @if (gautra.isActive) {
                          <div class="actions">
                            <button class="btn alt small" type="button"
                              (click)="openEditGautra(gautra, $event)">
                              Edit
                            </button>
                            <button class="btn alt small" type="button"
                              [attr.aria-expanded]="confirmingGautraId() === gautra.id"
                              (click)="askDeactivateGautra(gautra.id)">
                              Deactivate
                            </button>
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
        </div>
      }
    }

    <!-- Add / Edit modal -------------------------------------------------- -->
    @if (modalOpen()) {
      <div class="modal-backdrop" (click)="closeModal()">
        <div
          class="card modal-card modal-dialog"
          role="dialog"
          aria-modal="true"
          aria-labelledby="md-modal-title"
          (click)="$event.stopPropagation()"
          (keydown)="onModalKeydown($event)"
        >
          <h2 id="md-modal-title">{{ modalTitle() }}</h2>

          @if (isLookupTab(activeTab())) {
            <form (ngSubmit)="submitLookupModal()">
              @if (modalMode() === 'add') {
                <label for="modal-code">Code</label>
                <input id="modal-code" class="input" name="code" [(ngModel)]="lookupFormCode"
                  maxlength="50" placeholder="IN" #firstField />
              } @else {
                <p class="small">Code: {{ lookupFormCode }} (fixed once created)</p>
              }

              <label for="modal-label">Label</label>
              <input id="modal-label" class="input" name="label" [(ngModel)]="lookupFormLabel"
                maxlength="200" placeholder="India" #firstFieldEdit />

              <label for="modal-sort">Sort order</label>
              <input id="modal-sort" class="input" type="number" name="sortOrder"
                [(ngModel)]="lookupFormSortOrder" />

              <div class="actions">
                <button class="btn" type="submit"
                  [disabled]="busy() || !lookupFormLabel.trim() || (modalMode() === 'add' && !lookupFormCode.trim())">
                  {{ modalMode() === 'add' ? 'Add' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
              </div>
            </form>
          } @else if (activeTab() === 'Cities') {
            <form (ngSubmit)="submitCityModal()">
              <label for="modal-city-name">Name</label>
              <input id="modal-city-name" class="input" name="cityName" [(ngModel)]="cityFormName"
                maxlength="200" placeholder="Udaipur" #firstField />

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !cityFormName.trim()">
                  {{ modalMode() === 'add' ? 'Add' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
              </div>
            </form>
          } @else if (activeTab() === 'Localities') {
            <form (ngSubmit)="submitLocalityModal()">
              <label for="modal-locality-name">Name</label>
              <input id="modal-locality-name" class="input" name="localityName"
                [(ngModel)]="localityFormName" maxlength="200" placeholder="Hiran Magri" #firstField />

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !localityFormName.trim()">
                  {{ modalMode() === 'add' ? 'Add' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
              </div>
            </form>
          } @else if (activeTab() === 'Gautra') {
            <form (ngSubmit)="submitGautraModal()">
              <label for="modal-gautra-name">Name</label>
              <input id="modal-gautra-name" class="input" name="gautraName"
                [(ngModel)]="gautraFormName" maxlength="200" #firstField />

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !gautraFormName.trim()">
                  {{ modalMode() === 'add' ? 'Add' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
              </div>
            </form>
          }
        </div>
      </div>
    }
  `,
  styles: `
    .tabs {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
      border-bottom: 1px solid var(--line);
      margin-bottom: var(--space-4);
    }

    .tabs button {
      border: 0;
      background: transparent;
      padding: 10px 16px;
      font-family: inherit;
      font-size: 14px;
      font-weight: 500;
      color: var(--muted);
      cursor: pointer;
      border-bottom: 2px solid transparent;
      margin-bottom: -1px;
    }

    .tabs button.on {
      color: var(--ink);
      border-bottom-color: var(--accent);
      font-weight: 700;
    }

    .tabs button:hover:not(.on) {
      color: var(--ink);
    }

    .section-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      margin-bottom: var(--space-3);
    }

    .section-head h2 {
      margin: 0;
    }

    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(23, 32, 42, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--space-4);
      z-index: 200;
    }

    .modal-dialog {
      width: 100%;
      max-height: 90vh;
      overflow-y: auto;
    }
  `,
})
export class MasterDataComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

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

  readonly visibleTabs = computed(() => {
    const tabs = this.isSuperAdmin() ? ALL_TABS.filter((t) => t.value !== 'Gautra') : [];

    return [...tabs, { value: 'Gautra' as const, label: 'Gautra' }];
  });

  readonly activeTab = signal<TabKey>('Country');

  readonly activeTabLabel = computed(
    () => ALL_TABS.find((t) => t.value === this.activeTab())?.label ?? '',
  );

  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly busy = signal(false);

  // ---- Modal ----------------------------------------------------------------

  readonly modalOpen = signal(false);
  private modalTrigger: HTMLElement | null = null;

  @ViewChild('firstField') private firstFieldRef?: ElementRef<HTMLElement>;
  @ViewChild('firstFieldEdit') private firstFieldEditRef?: ElementRef<HTMLElement>;

  readonly modalMode = computed<'add' | 'edit'>(() => {
    switch (this.activeTab()) {
      case 'Cities':
        return this.editingCityId() === null ? 'add' : 'edit';
      case 'Localities':
        return this.editingLocalityId() === null ? 'add' : 'edit';
      case 'Gautra':
        return this.editingGautraId() === null ? 'add' : 'edit';
      default:
        return this.editingLookupId() === null ? 'add' : 'edit';
    }
  });

  readonly modalTitle = computed(() => {
    const verb = this.modalMode() === 'add' ? 'Add' : 'Edit';

    switch (this.activeTab()) {
      case 'Cities':
        return `${verb} city`;
      case 'Localities':
        return `${verb} locality`;
      case 'Gautra':
        return `${verb} Gautra`;
      default:
        return `${verb} ${this.activeTabLabel().toLowerCase()}`;
    }
  });

  // ---- Lookup lists -------------------------------------------------------

  readonly lookupItems = signal<readonly LookupItem[]>([]);
  readonly lookupLoading = signal(true);

  readonly editingLookupId = signal<string | null>(null);
  readonly confirmingLookupId = signal<string | null>(null);
  lookupFormCode = '';
  lookupFormLabel = '';
  lookupFormSortOrder = 0;

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
  cityFormName = '';

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
  localityFormName = '';

  // ---- Gautra -----------------------------------------------------------

  readonly gautras = signal<readonly Gautra[]>([]);
  readonly gautraLoading = signal(true);

  readonly editingGautraId = signal<string | null>(null);
  readonly confirmingGautraId = signal<string | null>(null);
  gautraFormName = '';

  ngOnInit(): void {
    this.activeTab.set(this.isSuperAdmin() ? 'Country' : 'Gautra');

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

  // ---- Tabs -----------------------------------------------------------------

  isLookupTab(tab: TabKey): tab is LookupCategory {
    return LOOKUP_TABS.some((t) => t.value === tab);
  }

  selectTab(tab: TabKey): void {
    if (this.activeTab() === tab) {
      return;
    }

    this.activeTab.set(tab);
    this.closeModal();
    this.confirmingLookupId.set(null);
    this.confirmingCityId.set(null);
    this.confirmingLocalityId.set(null);
    this.confirmingGautraId.set(null);

    if (this.isLookupTab(tab)) {
      this.loadLookupItems();
    }
  }

  // ---- Lookup lists -------------------------------------------------------

  private loadLookupItems(): void {
    this.lookupLoading.set(true);

    const category = this.activeTab() as LookupCategory;

    this.api.listLookupItems(category, true).subscribe({
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

  openAddLookup(event: MouseEvent): void {
    this.editingLookupId.set(null);
    this.lookupFormCode = '';
    this.lookupFormLabel = '';
    this.lookupFormSortOrder = 0;
    this.confirmingLookupId.set(null);
    this.openModal(event);
  }

  openEditLookup(item: LookupItem, event: MouseEvent): void {
    this.editingLookupId.set(item.id);
    this.lookupFormCode = item.code;
    this.lookupFormLabel = item.label;
    this.lookupFormSortOrder = item.sortOrder;
    this.confirmingLookupId.set(null);
    this.openModal(event);
  }

  submitLookupModal(): void {
    const label = this.lookupFormLabel.trim();

    if (label.length === 0) {
      return;
    }

    const editingId = this.editingLookupId();

    if (editingId === null) {
      const code = this.lookupFormCode.trim();

      if (code.length === 0) {
        return;
      }

      this.act(
        this.api.createLookupItem(this.activeTab() as LookupCategory, code, label, this.lookupFormSortOrder),
        `${label} added.`,
        () => {
          this.closeModal();
          this.loadLookupItems();
        },
      );

      return;
    }

    this.act(this.api.updateLookupItem(editingId, label, this.lookupFormSortOrder), `${label} updated.`, () => {
      this.closeModal();
      this.loadLookupItems();
    });
  }

  askDeactivateLookup(id: string): void {
    this.confirmingLookupId.set(this.confirmingLookupId() === id ? null : id);
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

  openAddCity(event: MouseEvent): void {
    if (this.selectedCountryId() === '') {
      return;
    }

    this.editingCityId.set(null);
    this.cityFormName = '';
    this.confirmingCityId.set(null);
    this.openModal(event);
  }

  openEditCity(city: City, event: MouseEvent): void {
    this.editingCityId.set(city.id);
    this.cityFormName = city.name;
    this.confirmingCityId.set(null);
    this.openModal(event);
  }

  submitCityModal(): void {
    const name = this.cityFormName.trim();
    const countryId = this.selectedCountryId();

    if (name.length === 0 || countryId === '') {
      return;
    }

    const editingId = this.editingCityId();

    if (editingId === null) {
      this.act(this.api.createCity(countryId, name), `${name} added.`, () => {
        this.closeModal();
        this.loadCities(countryId);
      });

      return;
    }

    this.act(this.api.updateCity(editingId, name), `${name} updated.`, () => {
      this.closeModal();
      this.loadCities(countryId);
    });
  }

  askDeactivateCity(id: string): void {
    this.confirmingCityId.set(this.confirmingCityId() === id ? null : id);
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

  openAddLocality(event: MouseEvent): void {
    if (this.selectedCityId() === '') {
      return;
    }

    this.editingLocalityId.set(null);
    this.localityFormName = '';
    this.confirmingLocalityId.set(null);
    this.openModal(event);
  }

  openEditLocality(locality: Locality, event: MouseEvent): void {
    this.editingLocalityId.set(locality.id);
    this.localityFormName = locality.name;
    this.confirmingLocalityId.set(null);
    this.openModal(event);
  }

  submitLocalityModal(): void {
    const name = this.localityFormName.trim();
    const cityId = this.selectedCityId();

    if (name.length === 0 || cityId === '') {
      return;
    }

    const editingId = this.editingLocalityId();

    if (editingId === null) {
      this.act(this.api.createLocality(cityId, name), `${name} added.`, () => {
        this.closeModal();
        this.loadLocalities(cityId);
      });

      return;
    }

    this.act(this.api.updateLocality(editingId, name), `${name} updated.`, () => {
      this.closeModal();
      this.loadLocalities(cityId);
    });
  }

  askDeactivateLocality(id: string): void {
    this.confirmingLocalityId.set(this.confirmingLocalityId() === id ? null : id);
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

  openAddGautra(event: MouseEvent): void {
    this.editingGautraId.set(null);
    this.gautraFormName = '';
    this.confirmingGautraId.set(null);
    this.openModal(event);
  }

  openEditGautra(gautra: Gautra, event: MouseEvent): void {
    this.editingGautraId.set(gautra.id);
    this.gautraFormName = gautra.name;
    this.confirmingGautraId.set(null);
    this.openModal(event);
  }

  submitGautraModal(): void {
    const name = this.gautraFormName.trim();

    if (name.length === 0) {
      return;
    }

    const editingId = this.editingGautraId();

    if (editingId === null) {
      this.act(this.api.createGautra(name), `${name} added.`, () => {
        this.closeModal();
        this.loadGautras();
      });

      return;
    }

    this.act(this.api.updateGautra(editingId, name), `${name} updated.`, () => {
      this.closeModal();
      this.loadGautras();
    });
  }

  askDeactivateGautra(id: string): void {
    this.confirmingGautraId.set(this.confirmingGautraId() === id ? null : id);
  }

  confirmDeactivateGautra(gautra: Gautra): void {
    this.act(this.api.deactivateGautra(gautra.id), `${gautra.name} deactivated.`, () => {
      this.confirmingGautraId.set(null);
      this.loadGautras();
    });
  }

  // ---- Modal ------------------------------------------------------------

  private openModal(event: MouseEvent): void {
    this.modalTrigger = event.currentTarget as HTMLElement;
    this.error.set(null);
    this.done.set(null);
    this.modalOpen.set(true);
    setTimeout(() => (this.firstFieldRef ?? this.firstFieldEditRef)?.nativeElement.focus());
  }

  closeModal(): void {
    if (!this.modalOpen()) {
      return;
    }

    this.modalOpen.set(false);

    const trigger = this.modalTrigger;
    this.modalTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  onModalKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closeModal();

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
