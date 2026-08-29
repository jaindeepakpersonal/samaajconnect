import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MembersApi } from './members.api';
import { Member } from './members.models';

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
 * The locality dropdown is built from what the directory actually returns
 * rather than the wireframe's three hardcoded names, which were prototype data.
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
        <a class="btn secondary" routerLink="/home">Back to home</a>
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
            <select class="input" id="member-locality" name="locality" [(ngModel)]="locality">
              <option value="">All localities</option>
              @for (option of localities(); track option) {
                <option [value]="option">{{ option }}</option>
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
            @if (term.length > 0 || locality.length > 0) {
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
                  <td>{{ member.locality ?? '—' }}</td>
                  <td>
                    <!-- Null means "not shared", which is not the same as
                         "none" - the service returns null rather than masking,
                         so this must not claim the member has no profession. -->
                    @if (member.profession; as profession) {
                      {{ profession }}
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

  readonly members = signal<readonly Member[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  term = '';
  locality = '';

  /**
   * Localities to offer, taken from the directory itself.
   *
   * The wireframe hardcoded three. Real ones come from the data, and a filter
   * offering a locality nobody lives in is a filter that returns nothing for no
   * visible reason.
   */
  readonly localities = signal<readonly string[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.search(this.term, this.locality).subscribe({
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

  /**
   * Adds any new localities to the dropdown without dropping ones a narrower
   * search filtered out - otherwise choosing a locality would empty the list
   * that offered it.
   */
  private rememberLocalities(found: readonly Member[]): void {
    const known = new Set(this.localities());

    for (const member of found) {
      if (member.locality !== null && member.locality.length > 0) {
        known.add(member.locality);
      }
    }

    this.localities.set([...known].sort((a, b) => a.localeCompare(b)));
  }
}
