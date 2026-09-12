import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MasterDataApi } from './master-data.api';
import { MembersApi } from './members.api';
import { Locality, LookupItem, Member } from './members.models';

/**
 * Members, from the member-portal wireframe's `#members` screen.
 *
 * The wireframe is a search box, two dropdowns - locality and profession - and
 * a table of Name / Locality / Profession / View.
 *
 * **The profession filter is not reproduced, and that is a privacy decision
 * rather than a missing feature.** Profession carries a per-field privacy
 * level: a member may keep it Private. A server-side filter on it would let
 * anybody confirm a private value one query at a time - ask for "CA", see who
 * comes back - which is the same reasoning that already stops the service
 * matching a search term against a private mobile number. The column stays,
 * because a member who chose to share it should be findable by eye; the filter
 * goes.
 *
 * **The locality filter is an exact `localityId` match now, not a substring on
 * free text.** Locality moved to a master-data-service pick on 2026-09-11, so
 * there is no longer a string to search toward - only a value to filter on. The
 * dropdown still keeps its own rule from before: it offers only localities
 * somebody in the results actually has, never the platform's full list, which
 * would be a filter that returns nothing for no visible reason. What changed is
 * how the option label reads - the full locality list is fetched once, from
 * master-data-service, purely to resolve an id on screen to the name a member
 * recognises.
 */
@Component({
  selector: 'app-members-list',
  imports: [FormsModule, RouterLink],
  styleUrl: './members.css',
  template: `
    <div class="members-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Members</h1>
          <p class="subtitle">Your Samaaj's member directory.</p>
        </div>
      </header>

      <div class="card">
        <form class="filters" (ngSubmit)="load()">
          <div>
            <label for="member-search">Search by name</label>
            <input
              class="input"
              id="member-search"
              name="term"
              [(ngModel)]="term"
              placeholder="Name"
            />
          </div>

          <div>
            <label for="member-locality">Locality</label>
            <select class="input" id="member-locality" name="locality" [(ngModel)]="localityId">
              <option value="">All localities</option>
              @for (option of localityOptions(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </div>

          <button class="btn" type="submit" [disabled]="loading()">
            {{ loading() ? 'Searching…' : 'Apply filters' }}
          </button>
        </form>

        @if (error(); as message) {
          <div class="notice error" role="alert">
            {{ message }}
            <button class="btn link" type="button" (click)="load()">Try again</button>
          </div>
        } @else if (loading()) {
          <p role="status">Loading the directory…</p>
        } @else if (members().length === 0) {
          <p class="notice info" role="status">
            @if (term.length > 0 || localityId.length > 0) {
              Nobody matches that. Try a different name or locality.
            } @else {
              This Samaaj's directory is empty.
            }
          </p>
        } @else {
          <div class="table-scroll">
            <table>
              <caption class="sr-only">Member directory</caption>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Locality</th>
                <th scope="col">Profession</th>
                <th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
              @for (member of members(); track member.id) {
                <tr>
                  <td>{{ member.fullName }}</td>
                  <td>{{ localityName(member.permanentAddress.localityId) }}</td>
                  <td>
                    <!-- Null means "not shared", which is not the same as
                         "none" - the service returns null rather than masking,
                         so this must not claim the member has no profession. -->
                    @if (member.professionId; as id) {
                      {{ professionName(id) }}
                    } @else {
                      <span class="small">Not shared</span>
                    }
                  </td>
                  <td>
                    <a class="btn small secondary" [routerLink]="['/members', member.id]">View</a>
                  </td>
                </tr>
              }
            </table>
          </div>

          <p class="small">
            {{ members().length }}
            {{ members().length === 1 ? 'member' : 'members' }} shown.
            Fields each member chose to keep private are not listed.
          </p>
        }
      </div>
    </div>
  `,
})
export class MembersListComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly master = inject(MasterDataApi);

  readonly members = signal<readonly Member[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  term = '';
  localityId = '';

  /** Every Locality on the platform, fetched once purely to resolve an id to a name. */
  private readonly allLocalities = signal<readonly Locality[]>([]);

  /** The ProfessionType lookup list, fetched once purely to resolve an id to a label. */
  private readonly professions = signal<readonly LookupItem[]>([]);

  /**
   * Locality ids to offer, taken from the directory itself.
   *
   * The wireframe hardcoded three names. Real ones come from the data, and a
   * filter offering a locality nobody lives in is a filter that returns
   * nothing for no visible reason.
   */
  private readonly knownLocalityIds = signal<readonly string[]>([]);

  readonly localityOptions = computed(() => {
    const byId = new Map(this.allLocalities().map((locality) => [locality.id, locality]));

    return this.knownLocalityIds()
      .map((id) => byId.get(id))
      .filter((locality): locality is Locality => locality !== undefined)
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  ngOnInit(): void {
    this.master.listLocalities().subscribe({
      next: (found) => this.allLocalities.set(found),
      error: () => {},
    });

    this.master.listProfessions().subscribe({
      next: (found) => this.professions.set(found),
      error: () => {},
    });

    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.search(this.term, this.localityId || null).subscribe({
      next: (found) => {
        this.members.set(found);
        this.rememberLocalities(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  localityName(localityId: string | null): string {
    if (localityId === null) {
      return '—';
    }

    return this.allLocalities().find((locality) => locality.id === localityId)?.name ?? '—';
  }

  professionName(professionId: string): string {
    return this.professions().find((profession) => profession.id === professionId)?.label ?? '—';
  }

  /**
   * Adds any new locality ids to the dropdown without dropping ones a
   * narrower search filtered out - otherwise choosing a locality would empty
   * the list that offered it.
   */
  private rememberLocalities(found: readonly Member[]): void {
    const known = new Set(this.knownLocalityIds());

    for (const member of found) {
      if (member.permanentAddress.localityId !== null) {
        known.add(member.permanentAddress.localityId);
      }
    }

    this.knownLocalityIds.set([...known]);
  }
}
