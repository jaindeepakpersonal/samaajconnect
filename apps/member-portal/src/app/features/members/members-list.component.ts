import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, DEFAULT_PAGE_SIZE, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from './master-data.api';
import { MembersApi } from './members.api';
import { Gautra, Locality, LookupItem, Member } from './members.models';

/**
 * Members - the Samaaj directory, as cards.
 *
 * The wireframe's `#members` screen was a table of Name / Locality /
 * Profession. Since 2026-10-24 each member is a card instead: photo, name with
 * Gautra, father and mother, profession and locality, and the filters are
 * name, Gautra, Locality and Profession.
 *
 * **Father and mother come from the member's household** (member-family-
 * service works them out from each relationship to the family admin). A card
 * whose household records no parent - or only one the viewer may not see,
 * such as a parent who took themselves out of the directory - leaves the line
 * off rather than printing a dash for a fact nobody stated.
 *
 * **The Profession filter exists now, and its privacy rule lives on the
 * server.** This screen used to omit it, because a filter on a field that can
 * be Private would let anybody confirm a private value one query at a time.
 * The search now matches only professions this viewer could already read on
 * the card, so a member who keeps theirs Private is never found by it - the
 * card's own "Not shared" is the most it ever reveals.
 *
 * **The dropdowns offer the whole master-data list**, not only values somebody
 * on the current page has. The directory pages now, so "values seen so far"
 * would be a list that changes as you page and never shows what is on page
 * five; an empty result says plainly that nobody matches.
 */
@Component({
  selector: 'app-members-list',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
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
            <label for="member-search">Name</label>
            <input class="input" id="member-search" name="term" [(ngModel)]="term" placeholder="Part of a name" />
          </div>

          <div>
            <label for="member-gautra">Gautra</label>
            <select class="input" id="member-gautra" name="gautra" [(ngModel)]="gautraId">
              <option value="">All Gautras</option>
              @for (option of gautras(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </div>

          <div>
            <label for="member-locality">Locality</label>
            <select class="input" id="member-locality" name="locality" [(ngModel)]="localityId">
              <option value="">All localities</option>
              @for (option of localities(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </div>

          <div>
            <label for="member-profession">Profession</label>
            <select class="input" id="member-profession" name="profession" [(ngModel)]="professionId">
              <option value="">All professions</option>
              @for (option of professions(); track option.id) {
                <option [value]="option.id">{{ option.label }}</option>
              }
            </select>
          </div>

          <button class="btn" type="submit" [disabled]="loading()">
            {{ loading() ? 'Searching…' : 'Apply filters' }}
          </button>
          @if (filtered()) {
            <button class="btn secondary" type="button" (click)="clear()">Clear</button>
          }
        </form>
      </div>

      @if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (loading()) {
        <p role="status">Loading the directory…</p>
      } @else if (members().length === 0) {
        <p class="notice info" role="status">
          @if (filtered()) {
            Nobody matches that. Try a different name or filter.
          } @else {
            This Samaaj's directory is empty.
          }
        </p>
      } @else {
        <ul class="member-cards" aria-label="Member directory">
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
                  <dd>
                    <!-- Null means "not shared", which is not "none". -->
                    @if (member.professionId; as id) {
                      {{ professionName(id) }}
                    } @else {
                      <span class="small">Not shared</span>
                    }
                  </dd>
                  <dt>Locality</dt>
                  <dd>{{ localityName(member.permanentAddress.localityId) }}</dd>
                </dl>
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

        <p class="small">Fields each member chose to keep private are not listed.</p>
      }
    </div>
  `,
})
export class MembersListComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly master = inject(MasterDataApi);

  readonly members = signal<readonly Member[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly hasMore = signal(false);

  readonly localities = signal<readonly Locality[]>([]);
  readonly gautras = signal<readonly Gautra[]>([]);
  readonly professions = signal<readonly LookupItem[]>([]);

  term = '';
  gautraId = '';
  localityId = '';
  professionId = '';

  /** What the last search asked for - the empty state reads from this, not from half-edited inputs. */
  readonly filtered = signal(false);

  ngOnInit(): void {
    // Each list resolves ids on the cards as well as filling its dropdown. A
    // failure leaves that dropdown empty and those ids unnamed, never the page.
    this.master.listLocalities().subscribe({
      next: (found) => this.localities.set([...found].sort((a, b) => a.name.localeCompare(b.name))),
      error: () => {},
    });

    this.master.listGautras().subscribe({
      next: (found) => this.gautras.set([...found].sort((a, b) => a.name.localeCompare(b.name))),
      error: () => {},
    });

    this.master.listProfessions().subscribe({
      next: (found) => this.professions.set([...found].sort((a, b) => a.label.localeCompare(b.label))),
      error: () => {},
    });

    this.load();
  }

  /** A new search starts again from the first page. */
  load(): void {
    this.goTo(1);
  }

  clear(): void {
    this.term = '';
    this.gautraId = '';
    this.localityId = '';
    this.professionId = '';
    this.load();
  }

  goTo(page: number): void {
    this.page.set(page);
    this.loading.set(true);
    this.error.set(null);
    this.filtered.set(
      this.term.trim().length > 0 || this.gautraId !== '' || this.localityId !== '' || this.professionId !== '',
    );

    this.api
      .searchPage(this.term.trim(), this.localityId || null, (page - 1) * DEFAULT_PAGE_SIZE, DEFAULT_PAGE_SIZE, {
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
          this.loading.set(false);
          this.error.set(describeError(failure));
        },
      });
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

  professionName(professionId: string): string {
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
