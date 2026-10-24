import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective, DEFAULT_PAGE_SIZE, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AdminMember, Gautra, Locality, LookupItem } from '../../core/admin.models';

/**
 * Members, from the admin wireframe's `#members` screen - as cards since
 * 2026-10-24, matching the member portal's directory: photo, name with Gautra,
 * father and mother, profession and locality, plus the contact details an
 * administrator sees. Filters are name, Gautra, Locality and Profession.
 *
 * The wireframe's **ID** ("MEM-00124") is still not here: a member's id is the
 * GUID from identity-tenant-service, not something read out over the phone,
 * and inventing a short code would be inventing an identifier nothing else on
 * the platform knows. **Status** is not member-family-service's to answer
 * either - whether an account is activated lives in identity-tenant-service,
 * and the pending ones have their own screen under Admin Users.
 *
 * **Father and mother come from the member's household**, worked out by
 * member-family-service from each relationship to the family admin; a card
 * whose household records neither leaves the lines off.
 *
 * **Profession is searchable now.** It carries a privacy level, which is why
 * this screen used to refuse a filter on it; the search now matches only
 * professions the viewer could already read, and an administrator reads every
 * field - so here it matches everybody, and in the member portal it never
 * confirms a Private one.
 *
 * **An Area Admin's Locality is fixed, not offered.** member-family-service
 * gives the administrator's view only to a search confined to their own
 * locality - anything wider is the member directory, which is not what this
 * screen is for.
 */
@Component({
  selector: 'app-member-list',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
  template: `
    <h1 class="title">Members</h1>
    <p class="sub">
      Everyone in this Samaaj, including members who have taken themselves out of the
      directory. Open one to correct their details.
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        Members belong to a Samaaj. Choose one in the top bar to see its directory.
      </p>
    } @else {
      <div class="card">
        <h2>Find somebody</h2>

        <form (ngSubmit)="search()">
          <div class="row">
            <div class="field">
              <label for="term">Name</label>
              <input class="input" id="term" name="term" [(ngModel)]="term" placeholder="Part of a name" />
            </div>

            <div class="field">
              <label for="gautra">Gautra</label>
              <select class="input" id="gautra" name="gautra" [(ngModel)]="gautraId">
                <option value="">All Gautras</option>
                @for (option of gautras(); track option.id) {
                  <option [value]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="locality">Locality</label>
              <select
                class="input"
                id="locality"
                name="locality"
                [(ngModel)]="localityId"
                [disabled]="areaLocalityId() !== null"
              >
                @if (areaLocalityId() === null) {
                  <option value="">All localities</option>
                }
                @for (option of localities(); track option.id) {
                  <option [value]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="profession">Profession</label>
              <select class="input" id="profession" name="profession" [(ngModel)]="professionId">
                <option value="">All professions</option>
                @for (option of professions(); track option.id) {
                  <option [value]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="loading()">Search</button>
              <button class="btn alt" type="button" (click)="clear()">Clear</button>
            </div>
          </div>
        </form>
      </div>

      @if (loading()) {
        <p class="empty" role="status">Loading the directory…</p>
      } @else if (members().length === 0) {
        <p class="empty">Nobody in this Samaaj matches that.</p>
      } @else {
        <ul class="member-cards" aria-label="Members of this Samaaj">
          @for (member of members(); track member.id) {
            <li class="card member-card">
              @if (member.photoUrl; as path) {
                <img class="member-photo" [scAuthedSrc]="path" alt="" />
              } @else {
                <span class="member-photo initials" aria-hidden="true">{{ initials(member.fullName) }}</span>
              }

              <div class="member-body">
                <h2 class="member-name">
                  <a [routerLink]="['/members', member.id]">{{ member.fullName }}</a>
                  @if (gautraName(member.gautraId); as gautra) {
                    <span class="member-gautra">({{ gautra }})</span>
                  }
                </h2>

                <dl class="member-facts">
                  @if (member.fatherName; as father) {
                    <dt>Father</dt>
                    <dd>{{ father }}</dd>
                  }
                  @if (member.motherName; as mother) {
                    <dt>Mother</dt>
                    <dd>{{ mother }}</dd>
                  }
                  <dt>Profession</dt>
                  <dd>{{ professionName(member.professionId) }}</dd>
                  <dt>Locality</dt>
                  <dd>{{ localityName(member.permanentAddress.localityId) }}</dd>
                  <dt>Mobile</dt>
                  <dd>{{ member.mobile ?? '—' }}</dd>
                  <dt>Email</dt>
                  <dd>{{ member.email ?? '—' }}</dd>
                </dl>

                <a class="btn alt" [routerLink]="['/members', member.id]">
                  Open<span class="sr-only"> {{ member.fullName }}</span>
                </a>
              </div>
            </li>
          }
        </ul>

        <nav class="pager" aria-label="Member pages">
          <p class="pager-summary">Page {{ page() }}</p>
          <div class="pager-controls">
            <button class="pager-btn" type="button" [disabled]="page() <= 1 || loading()" (click)="goTo(page() - 1)">
              Previous
            </button>
            <button class="pager-btn" type="button" [disabled]="!hasMore() || loading()" (click)="goTo(page() + 1)">
              Next
            </button>
          </div>
        </nav>
      }
    }
  `,
  styles: `
    .row {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .member-cards {
      list-style: none;
      margin: 0 0 16px;
      padding: 0;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
      gap: 16px;
    }

    .member-card {
      display: flex;
      gap: 12px;
      align-items: flex-start;
      margin: 0;
    }

    .member-photo {
      width: 72px;
      height: 72px;
      border-radius: 50%;
      object-fit: cover;
      flex-shrink: 0;
      background: var(--soft);
    }

    .member-photo.initials {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      font-size: 1.4rem;
      font-weight: 700;
      color: var(--accent-ink);
    }

    .member-body {
      min-width: 0;
      flex: 1;
    }

    .member-name {
      font-size: 1rem;
      margin: 0 0 8px;
      overflow-wrap: anywhere;
    }

    .member-gautra {
      font-weight: 400;
      color: var(--muted);
    }

    .member-facts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 2px 12px;
      margin: 0 0 12px;
      font-size: 14px;
    }

    .member-facts dt {
      font-weight: 600;
    }

    .member-facts dd {
      margin: 0;
      color: var(--muted);
      overflow-wrap: anywhere;
    }
  `,
})
export class MemberListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly members = signal<readonly AdminMember[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly hasMore = signal(false);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  readonly localities = signal<readonly Locality[]>([]);
  readonly gautras = signal<readonly Gautra[]>([]);
  readonly professions = signal<readonly LookupItem[]>([]);

  readonly areaLocalityId = computed(() => this.auth.user()?.areaLocalityId ?? null);

  term = '';
  gautraId = '';
  localityId = this.areaLocalityId() ?? '';
  professionId = '';

  ngOnInit(): void {
    this.api.listLocalities().subscribe({
      next: (found) => this.localities.set([...found].sort((a, b) => a.name.localeCompare(b.name))),
      error: () => {},
    });

    this.api.listLookupItems('ProfessionType').subscribe({
      next: (found) => this.professions.set([...found].sort((a, b) => a.label.localeCompare(b.label))),
      error: () => {},
    });

    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    // A Samaaj's own list, so only once there is a Samaaj to ask about.
    this.api.listGautras().subscribe({
      next: (found) => this.gautras.set([...found].sort((a, b) => a.name.localeCompare(b.name))),
      error: () => {},
    });

    this.search();
  }

  /** A new search starts again from the first page. */
  search(): void {
    this.goTo(1);
  }

  goTo(page: number): void {
    this.page.set(page);
    this.loading.set(true);
    this.error.set(null);

    this.api
      .searchMembers(this.term, this.localityId || null, (page - 1) * DEFAULT_PAGE_SIZE, DEFAULT_PAGE_SIZE, {
        gautraId: this.gautraId || null,
        professionId: this.professionId || null,
      })
      .subscribe({
        next: (found) => {
          this.members.set(found.items);
          this.hasMore.set(found.hasMore);
          this.loading.set(false);
        },
        error: (failure: unknown) => {
          this.error.set(describeError(failure));
          this.loading.set(false);
        },
      });
  }

  clear(): void {
    this.term = '';
    this.gautraId = '';
    this.localityId = this.areaLocalityId() ?? '';
    this.professionId = '';
    this.search();
  }

  localityName(localityId: string | null): string {
    if (localityId === null) {
      return '—';
    }

    return this.localities().find((locality) => locality.id === localityId)?.name ?? '—';
  }

  gautraName(gautraId: string | null): string | null {
    return gautraId === null ? null : (this.gautras().find((gautra) => gautra.id === gautraId)?.name ?? null);
  }

  professionName(professionId: string | null): string {
    if (professionId === null) {
      return '—';
    }

    return this.professions().find((profession) => profession.id === professionId)?.label ?? '—';
  }

  initials(name: string): string {
    return name
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }
}
