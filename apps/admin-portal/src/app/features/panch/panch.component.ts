import { DatePipe } from '@angular/common';
import { Component, ElementRef, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Election, ElectionInput, Panel, ResultsVisibility } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Samaaj Panch — who sits on it, and the elections that seat it.
 *
 * **The Panch is the Samaaj's own jury, elected from its own members.** Ten
 * seats; every member backs up to ten candidates and the ten most-backed take
 * the seats. An administrator convenes the election and runs its stages, but
 * never picks who wins — which is why this screen has "Set up an election" and
 * "Remove", and no "Appoint".
 *
 * **Publishing an election replaces the whole Panch**, and a member can also be
 * removed on their own. A removed member's seat stays empty until the next
 * election; nobody is promoted in their place.
 *
 * **Names come from the member directory**, because the service holds ids only.
 * A failure there leaves "A member" rather than a GUID and is not an error.
 *
 * **Both windows are validated here before the service does.** Voting cannot
 * start before nominations close: members who vote early have to see the same
 * ballot as members who vote late.
 */
@Component({
  selector: 'app-panch',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="title">Samaaj Panch</h1>
    <p class="sub">
      The ten members the Samaaj elects to judge its social issues. Members choose them;
      administrators only run the election.
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the Samaaj Panch module. Switch it on under the
        Samaaj's settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <h2>The Panch</h2>

        @if (panel(); as seated) {
          @if (seated.members.length === 0) {
            <p class="empty">
              Nobody is seated. Until an election is published, social issues are reviewed by
              administrators as before.
            </p>
          } @else {
            <p class="small">
              {{ seated.members.length }} of {{ seated.seats }} seats filled.
            </p>

            <div class="table-wrap">
              <table>
                <caption class="sr-only">Members seated on the Panch</caption>
                <thead>
                  <tr><th>Member</th><th>Seated</th><th></th></tr>
                </thead>
                <tbody>
                  @for (member of seated.members; track member.memberId) {
                    <tr>
                      <td><b><a [routerLink]="['/members', member.memberId]">{{ memberName(member.memberId) }}</a></b></td>
                      <td>{{ member.seatedAt | date: 'd MMM y' }}</td>
                      <td>
                        @if (confirmingRemoval() === member.memberId) {
                          <div class="row-actions">
                            <button class="btn small" type="button" [disabled]="busy()"
                              (click)="remove(member.memberId)">
                              Confirm removal
                            </button>
                            <button class="btn small alt" type="button"
                              (click)="confirmingRemoval.set(null)">
                              Keep
                            </button>
                          </div>
                        } @else {
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="confirmingRemoval.set(member.memberId)">
                            Remove<span class="sr-only"> {{ memberName(member.memberId) }}</span>
                          </button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <p class="small">
              A removed member's seat stays empty until the next election is published.
            </p>
          }
        }
      </div>

      <div class="card spaced">
        <div class="section-head">
          <h2>Elections</h2>
          <button class="btn" type="button" (click)="openModal($event)">Set up an election</button>
        </div>

        @if (elections().length === 0) {
          <p class="empty">
            None yet. An election runs nominations first and voting second, and starts as a draft
            with neither open.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Panch elections</caption>
              <thead>
                <tr>
                  <th>Election</th><th>Nominations</th><th>Voting</th>
                  <th>Ballot</th><th>Status</th><th><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (election of elections(); track election.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/panch', election.id]"><b>{{ election.title }}</b></a>
                    </td>
                    <td>
                      {{ election.nominationStartAt | date: 'd MMM' }} —
                      {{ election.nominationEndAt | date: 'd MMM y' }}
                      @if (election.acceptsNominations) {
                        <div class="muted">Open now</div>
                      }
                    </td>
                    <td>
                      {{ election.votingStartAt | date: 'd MMM' }} —
                      {{ election.votingEndAt | date: 'd MMM y' }}
                      @if (election.acceptsVotes) {
                        <div class="muted">Open now</div>
                      }
                    </td>
                    <td>{{ election.candidateCount }}</td>
                    <td>
                      <span class="pill" [class.warn]="election.status === 'Draft'">
                        {{ election.status }}
                      </span>
                    </td>
                    <td>
                      @if (confirmingDeletion() === election.id) {
                        <div class="row-actions" role="status">
                          <span class="small">
                            Delete {{ election.title }}@if (election.candidateCount > 0) {
                              and its {{ election.candidateCount }}
                              {{ election.candidateCount === 1 ? 'nomination' : 'nominations' }}
                            }? This cannot be undone.
                          </span>
                          <button class="btn small" type="button" [disabled]="busy()"
                            (click)="deleteElection(election)">
                            Delete for good
                          </button>
                          <button class="btn small alt" type="button"
                            (click)="confirmingDeletion.set(null)">
                            Keep
                          </button>
                        </div>
                      } @else if (isEditable(election)) {
                        <div class="row-actions">
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="openEdit($event, election)">
                            Edit<span class="sr-only"> {{ election.title }}</span>
                          </button>
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="confirmingDeletion.set(election.id)">
                            Delete<span class="sr-only"> {{ election.title }}</span>
                          </button>
                        </div>
                      } @else if (election.status === 'VotingOpen') {
                        <button class="btn small alt" type="button" [disabled]="busy()"
                          (click)="openExtend($event, election)">
                          Extend voting<span class="sr-only"> for {{ election.title }}</span>
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

      @if (modalOpen()) {
        <div class="modal-backdrop" (click)="closeModal()">
          <div
            class="card modal-card modal-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="election-modal-title"
            (click)="$event.stopPropagation()"
            (keydown)="onModalKeydown($event)"
          >
            <h2 id="election-modal-title">
              @switch (modalMode()) {
                @case ('edit') { Edit {{ editing()?.title }} }
                @case ('extend') { Extend voting }
                @default { Set up an election }
              }
            </h2>

            @if (modalMode() === 'extend') {
              <form (ngSubmit)="extend()">
                <p class="small">
                  Voting is open, so its close can only move later. Members have been told it
                  closes {{ editing()?.votingEndAt | date: 'd MMM y, h:mm a' }}; anybody planning to
                  vote before then still can.
                </p>

                <div class="field">
                  <label for="extend-end">Voting now closes</label>
                  <input id="extend-end" class="input" type="datetime-local" name="extendedEndAt"
                    [(ngModel)]="votingEndAt" #firstField
                    [attr.aria-invalid]="votingEndAt && !extendsLater() ? 'true' : null" />
                </div>

                @if (votingEndAt && !extendsLater()) {
                  <p class="notice warn" role="alert">
                    Choose a time after the current close. Voting cannot be cut short this way -
                    close it instead.
                  </p>
                }

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !extendsLater()">
                    Extend voting
                  </button>
                  <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
                </div>
              </form>
            } @else {
            <form (ngSubmit)="save()">
              <div class="form-grid">
                <div class="field">
                  <label for="election-title">Title</label>
                  <input id="election-title" class="input" name="title" [(ngModel)]="title"
                    maxlength="200" placeholder="Samaaj Panch 2026" #firstField />
                </div>
                <div class="field">
                  <label for="election-description">Description</label>
                  <input id="election-description" class="input" name="description"
                    [(ngModel)]="description" maxlength="2000" />
                </div>
              </div>

              <h3 class="section-heading">Nominations</h3>
              <div class="form-grid">
                <div class="field">
                  <label for="nom-start">Open</label>
                  <input id="nom-start" class="input" type="datetime-local" name="nominationStartAt"
                    [(ngModel)]="nominationStartAt" />
                </div>
                <div class="field">
                  <label for="nom-end">Close</label>
                  <input id="nom-end" class="input" type="datetime-local" name="nominationEndAt"
                    [(ngModel)]="nominationEndAt"
                    [attr.aria-invalid]="closesBeforeOpening(nominationStartAt, nominationEndAt) ? 'true' : null" />
                </div>

                @if (closesBeforeOpening(nominationStartAt, nominationEndAt)) {
                  <p class="notice warn span-all" role="alert">Nominations cannot close before they open.</p>
                }
              </div>

              <h3 class="section-heading">Voting</h3>
              <div class="form-grid">
                <div class="field">
                  <label for="vote-start">Open</label>
                  <input id="vote-start" class="input" type="datetime-local" name="votingStartAt"
                    [(ngModel)]="votingStartAt"
                    [attr.aria-invalid]="windowsOverlap() ? 'true' : null" />
                </div>
                <div class="field">
                  <label for="vote-end">Close</label>
                  <input id="vote-end" class="input" type="datetime-local" name="votingEndAt"
                    [(ngModel)]="votingEndAt"
                    [attr.aria-invalid]="closesBeforeOpening(votingStartAt, votingEndAt) ? 'true' : null" />
                </div>

                @if (closesBeforeOpening(votingStartAt, votingEndAt)) {
                  <p class="notice warn span-all" role="alert">Voting cannot close before it opens.</p>
                }

                @if (windowsOverlap()) {
                  <p class="notice warn span-all" role="alert">
                    Voting cannot start before nominations close. Members who vote early have to see
                    the same ballot as members who vote late.
                  </p>
                }

                <div class="field">
                  <label for="election-visibility">While voting is open, members see</label>
                  <select id="election-visibility" class="input" name="visibility"
                    [(ngModel)]="resultsVisibility">
                    <option value="HiddenUntilClose">No running count</option>
                    <option value="Live">The running count</option>
                  </select>
                </div>

                <p class="small span-all">
                  Every member backs up to ten candidates and the ten most-backed take the seats,
                  each with at least one vote. Publishing replaces the current Panch.
                </p>
              </div>

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !canSubmit()">
                  {{ modalMode() === 'edit' ? 'Save changes' : 'Create as a draft' }}
                </button>
                <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
              </div>
            </form>
            }
          </div>
        </div>
      }
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .section-heading {
      margin-top: var(--space-5);
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h2 {
      margin: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }
  `,
})
export class PanchComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly panel = signal<Panel | null>(null);
  readonly elections = signal<readonly Election[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly confirmingRemoval = signal<string | null>(null);
  readonly confirmingDeletion = signal<string | null>(null);

  /** What the one modal is doing: setting an election up, correcting one, or extending its voting. */
  readonly modalMode = signal<'create' | 'edit' | 'extend'>('create');

  /** The election being edited or extended. */
  readonly editing = signal<Election | null>(null);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  title = '';
  description = '';
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';
  resultsVisibility: ResultsVisibility = 'HiddenUntilClose';

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  readonly modalOpen = signal(false);

  /** The button that opened the modal, so focus can go back to it. */
  private modalTrigger: HTMLElement | null = null;

  ngOnInit(): void {
    this.load();
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  /** Whether voting would start before nominations close. Mirrors the service's own rule. */
  windowsOverlap(): boolean {
    if (!this.nominationEndAt || !this.votingStartAt) {
      return false;
    }

    return new Date(this.votingStartAt) < new Date(this.nominationEndAt);
  }

  /** A window that closes at or before the moment it opens. Both ends must be set. */
  closesBeforeOpening(open: string, close: string): boolean {
    if (!open || !close) {
      return false;
    }

    return new Date(close) <= new Date(open);
  }

  canSubmit(): boolean {
    return (
      this.title.trim().length > 0 &&
      this.nominationStartAt.length > 0 &&
      this.nominationEndAt.length > 0 &&
      this.votingStartAt.length > 0 &&
      this.votingEndAt.length > 0 &&
      !this.closesBeforeOpening(this.nominationStartAt, this.nominationEndAt) &&
      !this.closesBeforeOpening(this.votingStartAt, this.votingEndAt) &&
      !this.windowsOverlap()
    );
  }

  /**
   * Whether the service will take an edit or a deletion: a draft, or one
   * taking nominations. Mirrors panch-service's own rule, so the screen never
   * offers a button that always answers 409.
   */
  isEditable(election: Election): boolean {
    return election.status === 'Draft' || election.status === 'NominationsOpen';
  }

  /** Whether the new close is later than the one members were told. */
  extendsLater(): boolean {
    const current = this.editing();

    return (
      current !== null &&
      this.votingEndAt.length > 0 &&
      new Date(this.votingEndAt) > new Date(current.votingEndAt)
    );
  }

  openModal(event: MouseEvent): void {
    this.title = '';
    this.description = '';
    this.nominationStartAt = '';
    this.nominationEndAt = '';
    this.votingStartAt = '';
    this.votingEndAt = '';
    this.resultsVisibility = 'HiddenUntilClose';
    this.show('create', null, event);
  }

  openEdit(event: MouseEvent, election: Election): void {
    this.title = election.title;
    this.description = election.description ?? '';
    this.nominationStartAt = toLocalInputValue(election.nominationStartAt);
    this.nominationEndAt = toLocalInputValue(election.nominationEndAt);
    this.votingStartAt = toLocalInputValue(election.votingStartAt);
    this.votingEndAt = toLocalInputValue(election.votingEndAt);
    this.resultsVisibility = election.resultsVisibility;
    this.show('edit', election, event);
  }

  openExtend(event: MouseEvent, election: Election): void {
    this.votingEndAt = toLocalInputValue(election.votingEndAt);
    this.show('extend', election, event);
  }

  private show(mode: 'create' | 'edit' | 'extend', election: Election | null, event: MouseEvent): void {
    this.modalMode.set(mode);
    this.editing.set(election);
    this.confirmingDeletion.set(null);
    this.modalTrigger = event.currentTarget as HTMLElement;
    this.error.set(null);
    this.done.set(null);
    this.modalOpen.set(true);
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
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

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.panel().subscribe({
      next: (found) => {
        this.panel.set(found);
        this.loadElections();
        this.loadNames();
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

  private loadElections(): void {
    this.api.listElections().subscribe({
      next: (found) => {
        this.elections.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
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

  save(): void {
    if (!this.canSubmit()) {
      return;
    }

    const input: ElectionInput = {
      title: this.title.trim(),
      description: this.description.trim().length === 0 ? null : this.description.trim(),
      nominationStartAt: new Date(this.nominationStartAt).toISOString(),
      nominationEndAt: new Date(this.nominationEndAt).toISOString(),
      votingStartAt: new Date(this.votingStartAt).toISOString(),
      votingEndAt: new Date(this.votingEndAt).toISOString(),
      resultsVisibility: this.resultsVisibility,
    };

    const editing = this.modalMode() === 'edit' ? this.editing() : null;

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    const request = editing
      ? this.api.updateElection(editing.id, input)
      : this.api.createElection(input);

    request.subscribe({
      next: () => {
        this.done.set(
          editing
            ? `${input.title} saved.`
            : `${input.title} created as a draft. Nominations are not open yet.`,
        );
        this.busy.set(false);
        this.closeModal();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  extend(): void {
    const election = this.editing();

    if (election === null || !this.extendsLater()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.extendVoting(election.id, new Date(this.votingEndAt).toISOString()).subscribe({
      next: (updated) => {
        this.done.set(
          `Voting for ${updated.title} now closes ${new Date(updated.votingEndAt).toLocaleString('en-IN', {
            day: 'numeric',
            month: 'short',
            year: 'numeric',
            hour: 'numeric',
            minute: '2-digit',
          })}.`,
        );
        this.busy.set(false);
        this.closeModal();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  deleteElection(election: Election): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.deleteElection(election.id).subscribe({
      next: () => {
        this.confirmingDeletion.set(null);
        this.done.set(`${election.title} deleted.`);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  remove(memberId: string): void {
    const who = this.memberName(memberId);

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.removePanelMember(memberId).subscribe({
      next: (found) => {
        this.panel.set(found);
        this.confirmingRemoval.set(null);
        this.done.set(`${who} is no longer on the Panch.`);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}

/** An ISO instant to the local `datetime-local` input value it renders as. */
function toLocalInputValue(iso: string): string {
  const date = new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (n: number) => n.toString().padStart(2, '0');

  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}
