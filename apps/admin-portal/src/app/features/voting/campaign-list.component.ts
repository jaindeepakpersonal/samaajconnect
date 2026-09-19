import { DatePipe } from '@angular/common';
import { Component, ElementRef, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Campaign, CampaignInput, ResultsVisibility } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Celebrities / Voting — the campaigns, and setting one up.
 *
 * Wireframe `#celebrity`.
 *
 * **Members could nominate and vote in campaigns nobody could create.**
 * Creating one, moving it between stages, deciding a nomination and freezing
 * the result were four complete, tested, curl-only endpoints — and the first
 * three of them are what makes the fourth reachable at all, so the whole award
 * existed only as an API.
 *
 * **The two windows must not overlap, and the form says so before the service
 * does.** `CreateCampaignCommandValidator` refuses a voting window starting
 * before nominations close, because members who vote early must see the same
 * ballot as members who vote late. Offering a form that lets somebody build
 * that and discovering it on submit would be offering a shape the platform
 * does not have.
 *
 * **Results visibility is decided here or not at all.** Members who can see
 * who is winning vote differently from members who cannot; which a Samaaj
 * wants is theirs to choose, but it has to be chosen before voting opens
 * rather than discovered afterwards.
 *
 * **The wireframe's "Eligible voters: 1,104" is not here.** That count lives in
 * member-family-service, the directory call this panel makes is capped at a
 * hundred, and a number that is quietly wrong on any Samaaj larger than that is
 * worse than no number — the same reason the tenant list dropped its Members
 * column.
 */
@Component({
  selector: 'app-campaign-list',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="title">Celebrities / Voting</h1>
    <p class="sub">Configure a campaign, its ballot, its voting and its result.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the celebrity voting module. Switch it on under the
        Samaaj's settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <div class="section-head">
          <h2>Campaigns</h2>
          <button class="btn" type="button" (click)="openCreateModal($event)">
            Set up a campaign
          </button>
        </div>

        @if (campaigns().length === 0) {
          <p class="empty">
            None yet. A campaign runs nominations first and voting second, and starts as a draft
            with neither open.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Voting campaigns</caption>
              <thead>
                <tr>
                  <th>Campaign</th><th>Nominations</th><th>Voting</th>
                  <th>Ballot</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (campaign of campaigns(); track campaign.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/voting', campaign.id]"><b>{{ campaign.title }}</b></a>
                      <div class="muted">Top {{ campaign.topN }}</div>
                    </td>
                    <td>
                      {{ campaign.nominationStartAt | date: 'd MMM' }} —
                      {{ campaign.nominationEndAt | date: 'd MMM y' }}
                      @if (campaign.acceptsNominations) {
                        <div class="muted">Open now</div>
                      }
                    </td>
                    <td>
                      {{ campaign.votingStartAt | date: 'd MMM' }} —
                      {{ campaign.votingEndAt | date: 'd MMM y' }}
                      @if (campaign.acceptsVotes) {
                        <div class="muted">Open now</div>
                      }
                    </td>
                    <td>{{ campaign.candidateCount }}</td>
                    <td>
                      <span class="pill" [class.warn]="campaign.status === 'Draft'">
                        {{ campaign.status }}
                      </span>
                      @if (campaign.resultsVisibility === 'HiddenUntilClose') {
                        <div class="muted">Tally hidden</div>
                      }
                    </td>
                    <td>
                      <!-- Edit stops being offered once voting opens: the service refuses
                           it then, and a button that always answers 409 is a choice that
                           was never there. -->
                      @if (canEdit(campaign)) {
                        <button class="btn alt small" type="button"
                          (click)="openEditModal($event, campaign)">
                          Edit<span class="sr-only"> {{ campaign.title }}</span>
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <p class="small">
            "Open now" means the status says so <b>and</b> the clock agrees. A campaign left
            open past its closing date because nobody moved it on still stops taking votes.
          </p>
        }
      </div>

      <!-- Create / Edit modal --------------------------------------------- -->
      @if (modalOpen()) {
        <div class="modal-backdrop" (click)="closeModal()">
          <div
            class="card modal-card modal-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="campaign-modal-title"
            (click)="$event.stopPropagation()"
            (keydown)="onModalKeydown($event)"
          >
            <h2 id="campaign-modal-title">
              {{ modalMode() === 'create' ? 'Set up a campaign' : 'Edit ' + editingCampaign()?.title }}
            </h2>

            <form (ngSubmit)="submitModal()">
              <div class="form-grid">
                <div class="field">
                  <label for="campaign-title">Title</label>
                  <input id="campaign-title" class="input" name="title" [(ngModel)]="title"
                    maxlength="200" placeholder="2026 Samaaj Celebrity" #firstField />
                </div>

                <div class="field">
                  <label for="campaign-description">Description</label>
                  <input id="campaign-description" class="input" name="description"
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
                  <p class="notice span-all" role="alert">Nominations cannot close before they open.</p>
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
                  <p class="notice span-all" role="alert">Voting cannot close before it opens.</p>
                }

                @if (windowsOverlap()) {
                  <p class="notice span-all" role="alert">
                    Voting cannot start before nominations close. Members who vote early have to see
                    the same ballot as members who vote late.
                  </p>
                }

                <div class="field">
                  <label for="campaign-topn">Top</label>
                  <input id="campaign-topn" class="input" type="number" min="1" name="topN"
                    [(ngModel)]="topN" />
                </div>
                <div class="field">
                  <label for="campaign-visibility">While voting is open, members see</label>
                  <select id="campaign-visibility" class="input" name="visibility"
                    [(ngModel)]="resultsVisibility">
                    <option value="HiddenUntilClose">No running count</option>
                    <option value="Live">The running count</option>
                  </select>
                </div>

                <p class="small span-all">
                  Decide the count now: members who can see who is winning vote differently from
                  members who cannot. An administrator sees it either way, because somebody has to be
                  able to tell whether the thing is working.
                </p>
              </div>

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !canSubmit()">
                  {{ modalMode() === 'create' ? 'Create as a draft' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
              </div>
            </form>
          </div>
        </div>
      }
    }
  `,
  styles: `
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
  `,
})
export class CampaignListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly campaigns = signal<readonly Campaign[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  title = '';
  description = '';
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';
  topN = 10;
  resultsVisibility: ResultsVisibility = 'HiddenUntilClose';

  // ---- The create / edit modal, the same shape Events and Master Data use ---

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  readonly modalOpen = signal(false);
  readonly modalMode = signal<'create' | 'edit'>('create');
  readonly editingCampaign = signal<Campaign | null>(null);

  /** The button that opened the modal, so focus can go back to it. */
  private modalTrigger: HTMLElement | null = null;

  ngOnInit(): void {
    this.load();
  }

  /**
   * Whether voting would start before nominations close.
   *
   * Duplicated from `CreateCampaignCommandValidator` on purpose, and it has to
   * stay in step: a rule this form is stricter about is a campaign nobody can
   * create here, and one it is looser about is a wasted round trip that reads
   * as a bug.
   */
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
      this.topN >= 1 &&
      this.topN <= 100 &&
      !this.closesBeforeOpening(this.nominationStartAt, this.nominationEndAt) &&
      !this.closesBeforeOpening(this.votingStartAt, this.votingEndAt) &&
      !this.windowsOverlap()
    );
  }

  /** Editable while it is a draft or taking nominations; the service refuses it from voting on. */
  canEdit(campaign: Campaign): boolean {
    return campaign.status === 'Draft' || campaign.status === 'NominationsOpen';
  }

  openCreateModal(event: MouseEvent): void {
    this.modalMode.set('create');
    this.editingCampaign.set(null);
    this.title = '';
    this.description = '';
    this.nominationStartAt = '';
    this.nominationEndAt = '';
    this.votingStartAt = '';
    this.votingEndAt = '';
    this.topN = 10;
    this.resultsVisibility = 'HiddenUntilClose';
    this.openModal(event);
  }

  openEditModal(event: MouseEvent, campaign: Campaign): void {
    this.modalMode.set('edit');
    this.editingCampaign.set(campaign);
    this.title = campaign.title;
    this.description = campaign.description ?? '';
    this.nominationStartAt = toLocalInputValue(campaign.nominationStartAt);
    this.nominationEndAt = toLocalInputValue(campaign.nominationEndAt);
    this.votingStartAt = toLocalInputValue(campaign.votingStartAt);
    this.votingEndAt = toLocalInputValue(campaign.votingEndAt);
    this.topN = campaign.topN;
    this.resultsVisibility = campaign.resultsVisibility;
    this.openModal(event);
  }

  private openModal(event: MouseEvent): void {
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

  submitModal(): void {
    if (this.modalMode() === 'create') {
      this.create();
    } else {
      this.update();
    }
  }

  private input(): CampaignInput {
    return {
      title: this.title.trim(),
      description: blankToNull(this.description),
      nominationStartAt: new Date(this.nominationStartAt).toISOString(),
      nominationEndAt: new Date(this.nominationEndAt).toISOString(),
      votingStartAt: new Date(this.votingStartAt).toISOString(),
      votingEndAt: new Date(this.votingEndAt).toISOString(),
      topN: this.topN,
      resultsVisibility: this.resultsVisibility,
    };
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listCampaigns().subscribe({
      next: (found) => {
        this.campaigns.set(found);
        this.loading.set(false);
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

  create(): void {
    if (!this.canSubmit()) {
      return;
    }

    const input = this.input();

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.createCampaign(input).subscribe({
      next: () => {
        this.done.set(`${input.title} created as a draft. Nominations are not open yet.`);
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

  update(): void {
    const campaign = this.editingCampaign();

    if (campaign === null || !this.canSubmit()) {
      return;
    }

    const input = this.input();

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.updateCampaign(campaign.id, input).subscribe({
      next: () => {
        this.done.set(`${input.title} updated.`);
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
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
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
