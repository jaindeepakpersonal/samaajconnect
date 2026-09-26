import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { IssuesApi } from './issues.api';
import { LocalityOption, loadLocalityOptions } from './locality-options';
import {
  Issue,
  IssueCategories,
  IssueStatus,
  ProgressSteps,
  ReviewAccess,
  StatusLabels,
} from './issues.models';

type IssuesTab = 'community' | 'raise' | 'mine' | 'panch';

/**
 * Social Issues, from the member-portal wireframe's `#issues` screen.
 *
 * The wireframe's three panels - a submission form, the published list, and
 * "My Submissions" with a progress strip - are now `role="tablist"` tabs
 * rather than stacked cards, the same horizontal-tab pattern My Profile and
 * Matrimony's Proposals screen already use.
 *
 * **A fourth tab, "For the Panch", appears only for somebody who may see the
 * queue.** Reviewing used to be an administrator's job and lived in the admin
 * panel alone. Now it is the Samaaj Panch - an elected panel of ordinary
 * members - and a Panch member has no other screen to review from.
 * `reviewAccess()` decides whether the tab exists at all, so a member who
 * cannot see the queue is never shown a tab that answers 403.
 *
 * The strip is the interesting one. The wireframe draws four fixed steps -
 * Submitted, Under Review, Decision pending, Closed - with the reached ones
 * marked. Rejected and ChangesRequested are not on it: those are where an
 * issue *leaves* the path, and drawing a strip for those would say it is
 * partway to a decision when it is not going there at all, so those say so in
 * words instead.
 *
 * **Attach Evidence** is present and disabled. There is no upload endpoint and
 * no file storage on the platform (`DEVELOPMENT_PLAN.md` Phase 5), and the
 * wireframe-to-angular skill is explicit that a missing endpoint means build
 * the backend, never fake the call.
 */
@Component({
  selector: 'app-issues-list',
  imports: [FormsModule, RouterLink],
  styleUrl: './issues.css',
  template: `
    <div class="issues-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Social Issues</h1>
          <p class="subtitle">Judged by the Samaaj Panch once it has decided.</p>
        </div>
      </header>

      <div class="tabs" role="tablist" aria-label="Social issues">
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'community'"
          [attr.aria-selected]="activeTab() === 'community'"
          (click)="activeTab.set('community')"
        >
          Samaaj issues
        </button>
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'raise'"
          [attr.aria-selected]="activeTab() === 'raise'"
          (click)="activeTab.set('raise')"
        >
          Raise an issue
        </button>
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'mine'"
          [attr.aria-selected]="activeTab() === 'mine'"
          (click)="activeTab.set('mine')"
        >
          My submissions
        </button>
        @if (access(); as granted) {
          @if (granted.canSeeQueue) {
            <button
              type="button"
              role="tab"
              [class.on]="activeTab() === 'panch'"
              [attr.aria-selected]="activeTab() === 'panch'"
              (click)="openPanchTab()"
            >
              For the Panch
            </button>
          }
        }
      </div>

      @if (activeTab() === 'community') {
        <div class="card">
          <label for="filter-category">Filter by category</label>
          <select
            class="input"
            id="filter-category"
            name="filter"
            [(ngModel)]="filter"
            (ngModelChange)="load()"
          >
            <option value="">All categories</option>
            @for (option of categories; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>

          @if (loading()) {
            <p role="status">Loading…</p>
          } @else if (visible().length === 0) {
            <p class="small">Nothing is in front of the Panch, or decided, yet.</p>
          } @else {
            @for (issue of visible(); track issue.id) {
              <div class="issue-row">
                <a [routerLink]="['/issues', issue.id]">{{ issue.title }}</a>
                <div class="badges">
                  <span class="pill" [class]="pillClass(issue.status)">{{ label(issue.status) }}</span>
                  <span class="pill">{{ issue.category }}</span>
                </div>
              </div>
            }
          }
        </div>
      }

      @if (activeTab() === 'raise') {
        <form class="card" (ngSubmit)="submit(true)">
          <div class="form-grid">
            <div class="field">
              <label for="issue-title">Title</label>
              <input
                class="input"
                id="issue-title"
                name="title"
                [(ngModel)]="title"
                maxlength="200"
                placeholder="Road safety near the community school"
                required
              />
            </div>

            <div class="field">
              <label for="issue-category">Category</label>
              <select class="input" id="issue-category" name="category" [(ngModel)]="category">
                @for (option of categories; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="issue-locality">Locality (optional)</label>
              <select class="input" id="issue-locality" name="localityId" [(ngModel)]="localityId">
                <option value="">Not about one locality</option>
                @for (option of localities(); track option.id) {
                  <option [value]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="field span-all">
              <label for="issue-description">Describe the issue</label>
              <textarea
                class="input"
                id="issue-description"
                name="description"
                [(ngModel)]="description"
                rows="4"
                maxlength="5000"
                required
              ></textarea>
            </div>

            @if (formError(); as message) {
              <p class="notice error span-all" role="alert">{{ message }}</p>
            }

            @if (justSubmitted()) {
              <p class="notice info span-all" role="status">
                {{
                  submittedAsDraft()
                    ? 'Saved as a draft. Only you can see it until you submit it.'
                    : 'Sent for review. You will see it below with its progress.'
                }}
              </p>
            }

            <div class="actions span-all">
              <button class="btn secondary" type="button" disabled>Attach Evidence</button>
              <span class="small">
                Attachments are not switched on yet - the platform has no file storage.
              </span>
            </div>

            <div class="actions span-all">
              <button
                class="btn secondary"
                type="button"
                [disabled]="!canSubmit() || busy()"
                (click)="submit(false)"
              >
                Save draft
              </button>
              <button class="btn" type="submit" [disabled]="!canSubmit() || busy()">
                {{ busy() ? 'Sending…' : 'Submit for review' }}
              </button>
            </div>
          </div>
        </form>
      }

      @if (activeTab() === 'mine') {
        <div class="card">
          @if (error(); as message) {
            <div class="notice error" role="alert">
              {{ message }}
              <button class="btn link" type="button" (click)="load()">Try again</button>
            </div>
          } @else if (loading()) {
            <p role="status">Loading…</p>
          } @else if (mine().length === 0) {
            <p class="small">You have not raised anything yet.</p>
          } @else {
            @for (issue of mine(); track issue.id) {
              <div class="submission">
                <a [routerLink]="['/issues', issue.id]"><b>{{ issue.title }}</b></a>

                @if (isOnHappyPath(issue)) {
                  <div class="step" role="list" [attr.aria-label]="'Progress'">
                    @for (stage of steps; track stage) {
                      <span
                        role="listitem"
                        [class.done]="isReached(issue, stage)"
                        [class.on]="issue.status === stage"
                      >
                        {{ label(stage) }}
                      </span>
                    }
                  </div>
                } @else {
                  <!-- Off the path. A strip here would say the issue is partway
                       to a decision when it is not going there. -->
                  <div class="badges">
                    <span class="pill" [class]="pillClass(issue.status)">
                      {{ label(issue.status) }}
                    </span>
                  </div>
                }

                <p class="small">{{ describe(issue) }}</p>
              </div>
            }
          }
        </div>
      }

      @if (activeTab() === 'panch') {
        <div class="card">
          <p class="small">
            Waiting on the Panch, oldest first. Open one to record its decision.
          </p>

          @if (queueLoading()) {
            <p role="status">Loading…</p>
          } @else if (queueError(); as message) {
            <div class="notice error" role="alert">
              {{ message }}
              <button class="btn link" type="button" (click)="loadQueue()">Try again</button>
            </div>
          } @else if (queue().length === 0) {
            <p class="small">Nothing is waiting.</p>
          } @else {
            @for (issue of queue(); track issue.id) {
              <div class="issue-row">
                <a [routerLink]="['/issues', issue.id]">{{ issue.title }}</a>
                <div class="badges">
                  <span class="pill" [class]="pillClass(issue.status)">{{ label(issue.status) }}</span>
                  <span class="pill">{{ issue.category }}</span>
                  @if (issue.canDecide) {
                    <span class="pill ok">Ready for a decision</span>
                  }
                </div>
              </div>
            }
          }
        </div>
      }
    </div>
  `,
})
export class IssuesListComponent implements OnInit {
  private readonly api = inject(IssuesApi);
  private readonly masterData = inject(MasterDataApi);

  readonly activeTab = signal<IssuesTab>('community');

  readonly categories = IssueCategories;
  readonly steps = ProgressSteps;

  readonly issues = signal<readonly Issue[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly access = signal<ReviewAccess | null>(null);

  readonly queue = signal<readonly Issue[]>([]);
  readonly queueLoading = signal(false);
  readonly queueError = signal<string | null>(null);
  private queueLoaded = false;

  readonly busy = signal(false);
  readonly formError = signal<string | null>(null);
  readonly justSubmitted = signal(false);
  readonly submittedAsDraft = signal(false);

  title = '';
  description = '';
  category: string = IssueCategories[0];
  localityId = '';
  filter = '';

  /** Empty until master data answers, and on failure - the field is optional. */
  readonly localities = signal<readonly LocalityOption[]>([]);

  /**
   * What the panel says it shows: issues the Samaaj can see.
   *
   * Filtered on the status, not on `!isMine`. Doing it by author looked
   * equivalent - the service returns visible issues plus the caller's own -
   * but it is not: a member's *own* decided issue is excluded by it, so a
   * member whose issue had just been decided read "Nothing... yet" underneath
   * their own decided issue. It appears in both panels now, which is right,
   * because the two answer different questions: what the Samaaj can see, and
   * what I raised.
   */
  readonly visible = computed(() =>
    this.issues().filter(
      (issue) => issue.status === 'DecisionPending' || (issue.status === 'Closed' && issue.decidedAt !== null),
    ),
  );

  readonly mine = computed(() =>
    this.issues()
      .filter((issue) => issue.isMine)
      .slice()
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt)),
  );

  ngOnInit(): void {
    this.load();
    this.loadAccess();
    loadLocalityOptions(this.masterData).subscribe({
      next: (found) => this.localities.set(found),
      error: () => this.localities.set([]),
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.list(this.filter).subscribe({
      next: (found) => {
        this.issues.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /** Whether the "For the Panch" tab belongs on screen at all. */
  private loadAccess(): void {
    this.api.reviewAccess().subscribe({
      next: (found) => this.access.set(found),
      error: () => this.access.set(null),
    });
  }

  openPanchTab(): void {
    this.activeTab.set('panch');

    if (!this.queueLoaded) {
      this.loadQueue();
    }
  }

  loadQueue(): void {
    this.queueLoading.set(true);
    this.queueError.set(null);

    this.api.queue().subscribe({
      next: (found) => {
        this.queue.set(found);
        this.queueLoading.set(false);
        this.queueLoaded = true;
      },
      error: (failure: unknown) => {
        this.queueLoading.set(false);
        this.queueError.set(describeError(failure));
      },
    });
  }

  /**
   * A method rather than a `computed`: `title` and `description` are plain
   * fields bound with `[(ngModel)]`, and a computed over non-signals reads them
   * once and never again.
   */
  canSubmit(): boolean {
    return this.title.trim().length > 0 && this.description.trim().length > 0;
  }

  submit(submitNow: boolean): void {
    if (!this.canSubmit() || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    this.justSubmitted.set(false);

    const locality = this.localities().find((option) => option.id === this.localityId) ?? null;

    this.api
      .create(
        this.title.trim(),
        this.description.trim(),
        this.category,
        locality?.label ?? null,
        locality?.id ?? null,
        submitNow,
      )
      .subscribe({
        next: (created) => {
          this.issues.set([created, ...this.issues()]);
          this.title = '';
          this.description = '';
          this.localityId = '';
          this.submittedAsDraft.set(!submitNow);
          this.justSubmitted.set(true);
          this.busy.set(false);
        },
        error: (failure: unknown) => {
          this.formError.set(describeError(failure));
          this.busy.set(false);
        },
      });
  }

  // ---- Rendering --------------------------------------------------------

  label(status: IssueStatus): string {
    return StatusLabels[status];
  }

  /** Whether the strip describes where this issue is. */
  isOnHappyPath(issue: Issue): boolean {
    return ProgressSteps.includes(issue.status)
      && (issue.status !== 'Closed' || issue.decidedAt !== null);
  }

  /** A step is reached when the issue is at it or past it. */
  isReached(issue: Issue, stage: IssueStatus): boolean {
    return ProgressSteps.indexOf(issue.status) >= ProgressSteps.indexOf(stage);
  }

  pillClass(status: IssueStatus): string {
    if (status === 'Rejected') {
      return 'danger';
    }

    if (status === 'Closed') {
      return 'ok';
    }

    return status === 'ChangesRequested' ? 'warn' : '';
  }

  /** What is actually happening to this issue, in a sentence. */
  describe(issue: Issue): string {
    switch (issue.status) {
      case 'Draft':
        return 'Only you can see this. Submit it when you are ready.';
      case 'Submitted':
        return 'Waiting for a reviewer to pick it up.';
      case 'UnderReview':
        return 'A reviewer is looking at it.';
      case 'DecisionPending':
        return 'In front of the Samaaj Panch, awaiting its decision.';
      case 'ChangesRequested':
        return 'Sent back to you. Open it to see what was asked for.';
      case 'Rejected':
        return 'Not accepted. Open it to see why.';
      case 'Closed':
        return issue.decidedAt !== null ? 'Decided by the Panch. Open it to see the decision.' : 'Withdrawn.';
    }
  }
}
