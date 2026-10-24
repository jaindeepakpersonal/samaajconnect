import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { MembersApi } from '../members/members.api';
import { PanchApi } from './panch.api';
import { Election, Panel, stageLabel } from './panch.models';

/**
 * Samaaj Panch — who sits on it, and the elections that seat it.
 *
 * The Panch is the Samaaj's ten-member jury, elected by its own members. This
 * screen is where a member sees who they are, and the way in to nominate and
 * vote.
 *
 * **Names are resolved once, per seated member.** The service holds ids only, by
 * design, and the panel is at most ten people, so one directory call each is
 * cheap and bounded. A failed lookup leaves "A member" rather than an error.
 *
 * **Elections are read from the two flags as well as the status.** An election
 * is only open if its status says so *and* the clock agrees, and only the
 * server knows the time it is deciding against.
 */
@Component({
  selector: 'app-panch',
  imports: [RouterLink],
  styleUrl: './panch.css',
  template: `
    <div class="panch-page">
      <header class="page-head">
        <h1 class="page-title">Samaaj Panch</h1>
        <p class="subtitle">
          The ten members our Samaaj elects to judge its social issues.
        </p>
      </header>

      @if (loading()) {
        <p role="status">Loading the Panch…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        <h2 class="section-heading">The Panch</h2>

        @if (panel(); as seated) {
          @if (seated.viewerIsMember) {
            <p class="notice info" role="status">
              You sit on the Panch. You can review social issues and record decisions.
            </p>
          }

          @if (seated.members.length === 0) {
            <p class="notice info" role="status">
              No Panch is seated yet. Until an election is held, social issues are reviewed by
              the Samaaj's administrators.
            </p>
          } @else {
            <p class="small">{{ seated.members.length }} of {{ seated.seats }} seats filled.</p>

            <div class="grid">
              @for (member of seated.members; track member.memberId) {
                <div class="card" [class.mine]="member.memberId === me()">
                  <h2><a [routerLink]="['/members', member.memberId]">{{ nameFor(member.memberId) }}</a></h2>
                  <p class="small">Seated {{ date(member.seatedAt) }}</p>
                </div>
              }
            </div>
          }
        }

        <h2 class="section-heading">Elections</h2>

        @if (elections().length === 0) {
          <p class="notice info" role="status">
            Your Samaaj has not held a Panch election yet.
          </p>
        } @else {
          <div class="grid">
            @for (election of elections(); track election.id) {
              <div class="card">
                <h2>{{ election.title }}</h2>

                @if (election.description; as description) {
                  <p>{{ description }}</p>
                }

                <div class="badges">
                  <span class="pill" [class]="pillClass(election)">{{ stage(election) }}</span>

                  @if (election.myChoices !== null) {
                    <span class="pill ok">You have voted</span>
                  }
                </div>

                <div class="actions">
                  <a class="btn small" [routerLink]="['/panch', election.id]">
                    {{ action(election) }}
                  </a>
                </div>
              </div>
            }
          </div>
        }
      }
    </div>
  `,
})
export class PanchComponent implements OnInit {
  private readonly api = inject(PanchApi);
  private readonly auth = inject(AuthService);
  private readonly members = inject(MembersApi);

  readonly panel = signal<Panel | null>(null);
  readonly elections = signal<readonly Election[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly names = signal<Readonly<Record<string, string>>>({});

  readonly me = computed(() => this.auth.user()?.userId ?? null);

  ngOnInit(): void {
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.panel().subscribe({
      next: (found) => {
        this.panel.set(found);
        this.resolveNames(found.members.map((member) => member.memberId));
        this.loadElections();
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadElections(): void {
    this.api.list().subscribe({
      next: (found) => {
        this.elections.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private resolveNames(memberIds: readonly string[]): void {
    const known = this.names();

    for (const id of new Set(memberIds)) {
      if (id === this.me() || id in known) {
        continue;
      }

      this.members.get(id).subscribe({
        next: (member) => this.names.update((current) => ({ ...current, [id]: member.fullName })),
        error: () => {
          // Left unresolved; nameFor() falls back to a generic label.
        },
      });
    }
  }

  nameFor(memberId: string): string {
    return memberId === this.me() ? 'You' : (this.names()[memberId] ?? 'A member');
  }

  stage(election: Election): string {
    return stageLabel(election);
  }

  pillClass(election: Election): string {
    if (election.status === 'Published') {
      return 'ok';
    }

    return election.acceptsVotes || election.acceptsNominations ? 'warn' : '';
  }

  /** What the button says: what a member can do here right now. */
  action(election: Election): string {
    if (election.acceptsVotes && election.myChoices === null) {
      return 'Cast your ballot';
    }

    if (election.acceptsNominations) {
      return 'Put somebody forward';
    }

    return election.status === 'Published' ? 'View result' : 'View';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
