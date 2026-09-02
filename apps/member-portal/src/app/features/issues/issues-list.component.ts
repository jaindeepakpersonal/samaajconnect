import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { IssuesApi } from './issues.api';
import {
  Issue,
  IssueCategories,
  IssueStatus,
  ProgressSteps,
  StatusLabels,
} from './issues.models';

/**
 * Social Issues, from the member-portal wireframe's `#issues` screen.
 *
 * The wireframe has three panels and all three are here: a submission form, the
 * published list, and "My Submissions" with a progress strip.
 *
 * The strip is the interesting one. The wireframe draws four fixed steps -
 * Submitted, Under Review, Approved, Published - with the reached ones marked.
 * That is the happy path of an eight-state workflow, and three of the eight
 * are not on it: Rejected, ChangesRequested and Closed are where an issue
 * *leaves* the path. Drawing a strip for those would say an issue is partway
 * to publication when it is not going there at all, so those say so in words
 * instead.
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
          <p class="subtitle">Member submissions are published only after approval.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      <div class="grid2">
        <!-- Raise one ---------------------------------------------------- -->
        <form class="card" (ngSubmit)="submit(true)">
          <h2>Raise a social issue</h2>

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

          <label for="issue-category">Category</label>
          <select class="input" id="issue-category" name="category" [(ngModel)]="category">
            @for (option of categories; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>

          <label for="issue-locality">Locality (optional)</label>
          <input
            class="input"
            id="issue-locality"
            name="locality"
            [(ngModel)]="locality"
            maxlength="150"
            placeholder="Hiran Magri"
          />

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

          @if (formError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          @if (justSubmitted()) {
            <p class="notice info" role="status">
              {{
                submittedAsDraft()
                  ? 'Saved as a draft. Only you can see it until you submit it.'
                  : 'Sent for approval. You will see it below with its progress.'
              }}
            </p>
          }

          <div class="actions">
            <button class="btn secondary" type="button" disabled>Attach Evidence</button>
            <span class="small">
              Attachments are not switched on yet - the platform has no file storage.
            </span>
          </div>

          <div class="actions">
            <button
              class="btn secondary"
              type="button"
              [disabled]="!canSubmit() || busy()"
              (click)="submit(false)"
            >
              Save draft
            </button>
            <button class="btn" type="submit" [disabled]="!canSubmit() || busy()">
              {{ busy() ? 'Sending…' : 'Submit for approval' }}
            </button>
          </div>
        </form>

        <!-- Published ---------------------------------------------------- -->
        <div class="card">
          <h2>Published issues</h2>

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
          } @else if (published().length === 0) {
            <p class="small">Nothing has been published yet.</p>
          } @else {
            @for (issue of published(); track issue.id) {
              <div class="issue-row">
                <a [routerLink]="['/issues', issue.id]">{{ issue.title }}</a>
                <div class="badges">
                  <span class="pill">{{ label(issue.status) }}</span>
                  <span class="pill">{{ issue.category }}</span>
                </div>
              </div>
            }
          }
        </div>
      </div>

      <!-- Mine ------------------------------------------------------------ -->
      <div class="card mine">
        <h2>My submissions</h2>

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
                     to publication when it is not going there. -->
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
    </div>
  `,
})
export class IssuesListComponent implements OnInit {
  private readonly api = inject(IssuesApi);

  readonly categories = IssueCategories;
  readonly steps = ProgressSteps;

  readonly issues = signal<readonly Issue[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly busy = signal(false);
  readonly formError = signal<string | null>(null);
  readonly justSubmitted = signal(false);
  readonly submittedAsDraft = signal(false);

  title = '';
  description = '';
  category: string = IssueCategories[0];
  locality = '';
  filter = '';

  /**
   * What the panel says it shows: published issues.
   *
   * Filtered on the status, not on `!isMine`. Doing it by author looked
   * equivalent - the service returns published issues plus the caller's own -
   * but it is not: a member's *own* published issue is excluded by it, so a
   * member whose issue had just gone live read "Nothing has been published
   * yet" underneath their own published issue. It appears in both panels now,
   * which is right, because the two answer different questions: what the
   * Samaaj can see, and what I raised.
   */
  readonly published = computed(() =>
    this.issues().filter((issue) => issue.status === 'Published'),
  );

  readonly mine = computed(() =>
    this.issues()
      .filter((issue) => issue.isMine)
      .slice()
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt)),
  );

  ngOnInit(): void {
    this.load();
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

    const locality = this.locality.trim();

    this.api
      .create(
        this.title.trim(),
        this.description.trim(),
        this.category,
        locality.length > 0 ? locality : null,
        submitNow,
      )
      .subscribe({
        next: (created) => {
          this.issues.set([created, ...this.issues()]);
          this.title = '';
          this.description = '';
          this.locality = '';
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

  /** Whether the wireframe's four-step strip describes where this issue is. */
  isOnHappyPath(issue: Issue): boolean {
    return ProgressSteps.includes(issue.status);
  }

  /** A step is reached when the issue is at it or past it. */
  isReached(issue: Issue, stage: IssueStatus): boolean {
    return ProgressSteps.indexOf(issue.status) >= ProgressSteps.indexOf(stage);
  }

  pillClass(status: IssueStatus): string {
    if (status === 'Rejected') {
      return 'danger';
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
      case 'Approved':
        return 'Accepted, and awaiting publication.';
      case 'Published':
        return 'Visible to your Samaaj.';
      case 'ChangesRequested':
        return 'Sent back to you. Open it to see what was asked for.';
      case 'Rejected':
        return 'Not accepted. Open it to see why.';
      case 'Closed':
        return 'Closed.';
    }
  }
}
