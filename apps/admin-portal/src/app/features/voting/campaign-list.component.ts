import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Campaign, ResultsVisibility } from '../../core/admin.models';
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
        <h3>Campaigns</h3>

        @if (campaigns().length === 0) {
          <p class="empty">
            None yet. A campaign runs nominations first and voting second, and starts as a draft
            with neither open.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Campaign</th><th>Nominations</th><th>Voting</th>
                  <th>Ballot</th><th>Status</th>
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

      <!-- Create --------------------------------------------------------- -->
      <div class="card spaced">
        <h3>Set up a campaign</h3>

        <form (ngSubmit)="create()">
          <label for="campaign-title">Title</label>
          <input id="campaign-title" class="input" name="title" [(ngModel)]="title"
            maxlength="200" placeholder="2026 Samaaj Celebrity" />

          <label for="campaign-description">Description</label>
          <input id="campaign-description" class="input" name="description"
            [(ngModel)]="description" maxlength="2000" />

          <h3 class="section-heading">Nominations</h3>
          <div class="filter-row">
            <div>
              <label for="nom-start">Open</label>
              <input id="nom-start" class="input" type="datetime-local" name="nominationStartAt"
                [(ngModel)]="nominationStartAt" />
            </div>
            <div>
              <label for="nom-end">Close</label>
              <input id="nom-end" class="input" type="datetime-local" name="nominationEndAt"
                [(ngModel)]="nominationEndAt" />
            </div>
          </div>

          <h3 class="section-heading">Voting</h3>
          <div class="filter-row">
            <div>
              <label for="vote-start">Open</label>
              <input id="vote-start" class="input" type="datetime-local" name="votingStartAt"
                [(ngModel)]="votingStartAt"
                [attr.aria-invalid]="windowsOverlap() ? 'true' : null" />
            </div>
            <div>
              <label for="vote-end">Close</label>
              <input id="vote-end" class="input" type="datetime-local" name="votingEndAt"
                [(ngModel)]="votingEndAt" />
            </div>
          </div>

          @if (windowsOverlap()) {
            <p class="notice" role="alert">
              Voting cannot start before nominations close. Members who vote early have to see
              the same ballot as members who vote late.
            </p>
          }

          <div class="filter-row">
            <div>
              <label for="campaign-topn">Top</label>
              <input id="campaign-topn" class="input" type="number" min="1" name="topN"
                [(ngModel)]="topN" />
            </div>
            <div>
              <label for="campaign-visibility">While voting is open, members see</label>
              <select id="campaign-visibility" class="input" name="visibility"
                [(ngModel)]="resultsVisibility">
                <option value="HiddenUntilClose">No running count</option>
                <option value="Live">The running count</option>
              </select>
            </div>
          </div>

          <p class="small">
            Decide the count now: members who can see who is winning vote differently from
            members who cannot. An administrator sees it either way, because somebody has to be
            able to tell whether the thing is working.
          </p>

          <button class="btn" type="submit" [disabled]="busy() || !canCreate()">
            Create as a draft
          </button>
        </form>
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .section-heading {
      margin-top: var(--space-5);
    }

    .filter-row {
      display: flex;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .filter-row > div {
      flex: 1 1 200px;
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

  canCreate(): boolean {
    return (
      this.title.trim().length > 0 &&
      this.nominationStartAt.length > 0 &&
      this.nominationEndAt.length > 0 &&
      this.votingStartAt.length > 0 &&
      this.votingEndAt.length > 0 &&
      this.topN > 0 &&
      !this.windowsOverlap()
    );
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
    if (!this.canCreate()) {
      return;
    }

    const title = this.title.trim();

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api
      .createCampaign({
        title,
        description: blankToNull(this.description),
        nominationStartAt: new Date(this.nominationStartAt).toISOString(),
        nominationEndAt: new Date(this.nominationEndAt).toISOString(),
        votingStartAt: new Date(this.votingStartAt).toISOString(),
        votingEndAt: new Date(this.votingEndAt).toISOString(),
        topN: this.topN,
        resultsVisibility: this.resultsVisibility,
      })
      .subscribe({
        next: () => {
          this.done.set(`${title} created as a draft. Nominations are not open yet.`);
          this.busy.set(false);
          this.title = '';
          this.description = '';
          this.nominationStartAt = '';
          this.nominationEndAt = '';
          this.votingStartAt = '';
          this.votingEndAt = '';
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
