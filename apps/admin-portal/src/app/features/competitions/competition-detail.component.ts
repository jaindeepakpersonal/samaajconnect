import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import {
  Competition,
  CompetitionDetail,
  CompetitionResult,
  CompetitionStatus,
  RoundDetail,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One competition: its stage, its judges, its rounds, and a selected round's
 * entries and result.
 *
 * No wireframe covers this; the module is new this cycle. Shaped after
 * Celebrities / Voting's own campaign detail screen, with one deliberate
 * difference: this screen never shows a per-entry mark to anyone but the
 * organiser (recording one is a judge's own job, on member-portal) - what
 * this screen offers is creating rounds, creating entries, closing a round
 * and publishing its result.
 */
@Component({
  selector: 'app-competition-detail',
  imports: [DatePipe, FormsModule, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/competitions">← All competitions</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such competition in {{ scope.label() }}.</p>
    } @else if (competition(); as subject) {
      <h1 class="title">{{ subject.title }}</h1>
      <p class="sub">
        {{ subject.status }} · {{ subject.registrationCount }} registered ·
        {{ detail()?.rounds?.length ?? 0 }} round(s)
      </p>

      @if (subject.description) {
        <p>{{ subject.description }}</p>
      }

      <!-- Stage ---------------------------------------------------------- -->
      <div class="card">
        <h2>Stage</h2>

        <p class="small">
          Registration {{ subject.registrationStartAt | date: 'd MMM' }} —
          {{ subject.registrationEndAt | date: 'd MMM y' }}
        </p>

        @if (nextStatus(); as next) {
          <p class="small">
            Draft → registration → closed to registration → in progress → closed, and never
            backwards.
          </p>

          <button class="btn" type="button" [disabled]="busy()" (click)="move(next)">
            {{ moveLabel(next) }}
          </button>
        } @else {
          <p class="empty">Closed. This competition is finished.</p>
        }
      </div>

      <!-- Judges ----------------------------------------------------------- -->
      <div class="card spaced">
        <h2>Judges</h2>

        @if (judges().length === 0) {
          <p class="empty">Nobody assigned yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Assigned judges</caption>
              <thead><tr><th>Member</th><th>Assigned</th><th></th></tr></thead>
              <tbody>
                @for (judge of judges(); track judge.id) {
                  <tr>
                    <td><b><a [routerLink]="['/members', judge.memberId]">{{ memberName(judge.memberId) }}</a></b></td>
                    <td>{{ judge.assignedAt | date: 'd MMM y' }}</td>
                    <td>
                      <button class="btn small alt" type="button" [disabled]="busy()"
                        (click)="removeJudge(judge.memberId)">
                        Remove
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <form class="inline-form" (ngSubmit)="assignJudge()">
          <label class="sr-only" for="judge-picker">Assign a judge</label>
          <select id="judge-picker" class="input" name="judgeMemberId" [(ngModel)]="judgeMemberId">
            <option value="">Choose a member…</option>
            @for (member of members(); track member.id) {
              <option [value]="member.id">{{ member.fullName }}</option>
            }
          </select>
          <button class="btn small" type="submit" [disabled]="busy() || !judgeMemberId">
            Assign as judge
          </button>
        </form>
      </div>

      <!-- Rounds ------------------------------------------------------------ -->
      <div class="card spaced">
        <h2>Rounds</h2>

        <p class="small">
          There is no automatic advancement between rounds - create an entry for each
          participant you want competing in a round, by hand, in every round including the
          first.
        </p>

        @if (rounds().length === 0) {
          <p class="empty">None yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">This competition's rounds</caption>
              <thead>
                <tr><th>Round</th><th>Entries</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                @for (round of rounds(); track round.id) {
                  <tr [class.selected]="selectedRoundId() === round.id">
                    <td>
                      <button class="btn link" type="button" (click)="selectRound(round.id)">
                        <b>{{ round.name }}</b>
                      </button>
                    </td>
                    <td>{{ round.entryCount }}</td>
                    <td>
                      <span class="pill" [class.warn]="round.status === 'Open'">
                        {{ round.status }}
                      </span>
                      @if (round.hasPublishedResult) {
                        <div class="muted">Published</div>
                      }
                    </td>
                    <td>
                      <div class="row-actions">
                        @if (round.status === 'Open') {
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="closeRound(round.id)">
                            Close
                          </button>
                        } @else if (!round.hasPublishedResult) {
                          <button class="btn small" type="button" [disabled]="busy()"
                            (click)="publish(round.id)">
                            Publish result
                          </button>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <form class="inline-form spaced" (ngSubmit)="createRound()">
          <label class="sr-only" for="round-name">New round name</label>
          <input id="round-name" class="input" name="roundName" [(ngModel)]="roundName"
            maxlength="100" placeholder="Round name (e.g. Final)" />
          <button class="btn small" type="submit" [disabled]="busy() || !roundName.trim()">
            Open round
          </button>
        </form>
      </div>

      <!-- Selected round: entries and result --------------------------------- -->
      @if (selectedRoundId(); as roundId) {
        <div class="card spaced">
          <h2>{{ selectedRound()?.name ?? 'Round' }}</h2>

          @if (roundLoading()) {
            <p class="empty" role="status">Loading…</p>
          } @else if (roundDetail(); as rd) {
            @if (rd.entries.length === 0) {
              <p class="empty">No entries yet.</p>
            } @else {
              <div class="table-wrap">
                <table>
                  <caption class="sr-only">Entries in this round</caption>
                  <thead><tr><th>Participant</th><th>Average score</th></tr></thead>
                  <tbody>
                    @for (entry of rd.entries; track entry.id) {
                      <tr>
                        <td><b><a [routerLink]="['/members', entry.participantMemberId]">{{ memberName(entry.participantMemberId) }}</a></b></td>
                        <td>{{ entry.averageScore ?? '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }

            @if (selectedRound()?.status === 'Open') {
              <form class="inline-form spaced" (ngSubmit)="createEntry(roundId)">
                <label class="sr-only" for="entry-picker">Add a participant</label>
                <select id="entry-picker" class="input" name="entryMemberId"
                  [(ngModel)]="entryMemberId">
                  <option value="">Choose a registered member…</option>
                  @for (memberId of unenteredRegistrants(); track memberId) {
                    <option [value]="memberId">{{ memberName(memberId) }}</option>
                  }
                </select>
                <button class="btn small" type="submit" [disabled]="busy() || !entryMemberId">
                  Create entry
                </button>
              </form>
            }
          }

          @if (result(); as ranking) {
            <div class="spaced">
              <h3 class="section-heading">Published result</h3>
              <p class="small">
                Announced {{ ranking.publishedAt | date: 'd MMM y, h:mm a' }}. Stored as it was
                computed and never recalculated.
              </p>

              <div class="table-wrap">
                <table>
                  <caption class="sr-only">The published ranking, in rank order</caption>
                  <thead><tr><th>#</th><th>Participant</th><th>Average</th></tr></thead>
                  <tbody>
                    @for (entry of ranking.ranking; track entry.entryId) {
                      <tr>
                        <td>{{ entry.rank }}</td>
                        <td><b><a [routerLink]="['/members', entry.participantMemberId]">{{ memberName(entry.participantMemberId) }}</a></b></td>
                        <td>{{ entry.averageScore }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>
          }
        </div>
      }
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .section-heading {
      margin-top: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }

    .inline-form {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
    }

    tr.selected {
      background: var(--surface-alt, rgba(0, 0, 0, 0.03));
    }
  `,
})
export class CompetitionDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly detail = signal<CompetitionDetail | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly selectedRoundId = signal<string | null>(null);
  readonly roundDetail = signal<RoundDetail | null>(null);
  readonly roundLoading = signal(false);
  readonly result = signal<CompetitionResult | null>(null);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly members = computed(() =>
    [...this.names().entries()].map(([id, fullName]) => ({ id, fullName })),
  );

  readonly competition = computed<Competition | null>(() => this.detail()?.competition ?? null);
  readonly rounds = computed(() => this.detail()?.rounds ?? []);
  readonly judges = computed(() => this.detail()?.judges ?? []);

  readonly selectedRound = computed(
    () => this.rounds().find((r) => r.id === this.selectedRoundId()) ?? null,
  );

  readonly enteredMemberIds = computed(
    () => new Set((this.roundDetail()?.entries ?? []).map((e) => e.participantMemberId)),
  );

  readonly unenteredRegistrants = computed(() =>
    (this.detail()?.registeredMemberIds ?? []).filter((id) => !this.enteredMemberIds().has(id)),
  );

  judgeMemberId = '';
  roundName = '';
  entryMemberId = '';

  /** The one stage this competition can move to, or null when it is finished. */
  readonly nextStatus = computed<CompetitionStatus | null>(() => {
    switch (this.competition()?.status) {
      case 'Draft':
        return 'RegistrationOpen';
      case 'RegistrationOpen':
        return 'RegistrationClosed';
      case 'RegistrationClosed':
        return 'InProgress';
      case 'InProgress':
        return 'Closed';
      default:
        return null;
    }
  });

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  moveLabel(next: CompetitionStatus): string {
    switch (next) {
      case 'RegistrationOpen':
        return 'Open registration';
      case 'RegistrationClosed':
        return 'Close registration';
      case 'InProgress':
        return 'Start judging';
      default:
        return 'Close competition';
    }
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.competition(this.id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
        this.loadNames();
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.notFound.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  selectRound(roundId: string): void {
    this.selectedRoundId.set(roundId);
    this.entryMemberId = '';
    this.loadRound(roundId);
  }

  private loadRound(roundId: string): void {
    this.roundLoading.set(true);
    this.result.set(null);

    this.api.round(this.id, roundId).subscribe({
      next: (found) => {
        this.roundDetail.set(found);
        this.roundLoading.set(false);

        if (found.round.hasPublishedResult) {
          this.loadResult(roundId);
        }
      },
      error: () => {
        this.roundDetail.set(null);
        this.roundLoading.set(false);
      },
    });
  }

  /** A 404 before a result exists is the normal state, not an error. */
  private loadResult(roundId: string): void {
    this.api.roundResult(this.id, roundId).subscribe({
      next: (found) => this.result.set(found),
      error: () => this.result.set(null),
    });
  }

  move(status: CompetitionStatus): void {
    this.act(this.api.moveCompetition(this.id, status), `Competition is now ${status}.`);
  }

  assignJudge(): void {
    if (!this.judgeMemberId) {
      return;
    }

    const who = this.memberName(this.judgeMemberId);

    this.act(this.api.assignJudge(this.id, this.judgeMemberId, true), `${who} is now a judge.`);
    this.judgeMemberId = '';
  }

  removeJudge(memberId: string): void {
    const who = this.memberName(memberId);

    this.act(this.api.assignJudge(this.id, memberId, false), `${who} is no longer a judge.`);
  }

  createRound(): void {
    const name = this.roundName.trim();

    if (!name) {
      return;
    }

    this.act(this.api.createRound(this.id, name), `${name} opened.`);
    this.roundName = '';
  }

  closeRound(roundId: string): void {
    this.act(this.api.closeRound(this.id, roundId), 'Round closed.');
  }

  createEntry(roundId: string): void {
    if (!this.entryMemberId) {
      return;
    }

    const who = this.memberName(this.entryMemberId);
    const entryMemberId = this.entryMemberId;
    this.entryMemberId = '';

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.createEntry(this.id, roundId, entryMemberId).subscribe({
      next: () => {
        this.done.set(`${who} entered.`);
        this.busy.set(false);
        this.load();
        this.loadRound(roundId);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  publish(roundId: string): void {
    this.act(this.api.publishRoundResults(this.id, roundId), 'The result is announced.');
  }

  /**
   * Every action re-reads the competition, the same reasoning
   * Celebrities/Voting's own detail screen already uses: an assign, a move or
   * a publish changes what the rest of the screen may offer, and the server
   * is the only thing that knows all of it at once.
   */
  private act(work: { subscribe: (o: object) => void }, message: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        this.load();

        const roundId = this.selectedRoundId();

        if (roundId) {
          this.loadRound(roundId);
        }
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
