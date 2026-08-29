import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { VotingApi } from './voting.api';
import { Campaign, stageLabel } from './voting.models';

/**
 * Celebrities of Samaaj, from the member-portal wireframe's `#celebrity`
 * screen.
 *
 * The wireframe shows one campaign's ballot with Vote buttons and a "View Past
 * Results" link. A Samaaj runs a campaign a year, so the list of campaigns is
 * the way in and the ballot lives on the detail screen - which is also what
 * makes "past results" a campaign rather than a separate page.
 *
 * The wireframe's fixed notice - "Voting closes 20 Sep 2026. One vote per
 * eligible member" - becomes a per-campaign line, because both halves are per
 * campaign: the closing date is on the campaign, and the top-N is configurable
 * rather than the wireframe's hardcoded ten.
 *
 * **The screen reads `acceptsVotes` and `acceptsNominations`, never the status
 * alone.** A campaign is only open if its status says so *and* the clock
 * agrees, and only the server knows the time it is deciding against. Deriving
 * from the status here would offer a Vote button on a campaign whose voting
 * window closed an hour ago.
 */
@Component({
  selector: 'app-campaigns-list',
  imports: [RouterLink],
  styleUrl: './voting.css',
  template: `
    <div class="voting-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Celebrities of Samaaj</h1>
          <p class="subtitle">Nominate members, and cast your one vote.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (loading()) {
        <p role="status">Loading campaigns…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (campaigns().length === 0) {
        <p class="notice info" role="status">
          Your Samaaj has not run a Celebrities of Samaaj campaign yet.
        </p>
      } @else {
        @if (current().length > 0) {
          <div class="grid">
            @for (campaign of current(); track campaign.id) {
              <div class="card">
                <h3>{{ campaign.title }}</h3>

                @if (campaign.description; as description) {
                  <p>{{ description }}</p>
                }

                <div class="badges">
                  <span class="pill" [class]="pillClass(campaign)">{{ stage(campaign) }}</span>

                  @if (campaign.myVoteCandidateId !== null) {
                    <span class="pill ok">You have voted</span>
                  }
                </div>

                <p class="small">{{ describe(campaign) }}</p>

                <div class="actions">
                  <a class="btn small" [routerLink]="['/voting', campaign.id]">
                    {{ action(campaign) }}
                  </a>
                </div>
              </div>
            }
          </div>
        }

        @if (past().length > 0) {
          <!-- The wireframe's "View Past Results". A finished campaign is not a
               different page, it is a campaign whose result is frozen. -->
          <h2 class="section-heading">Past campaigns</h2>

          <div class="grid">
            @for (campaign of past(); track campaign.id) {
              <div class="card">
                <h3>{{ campaign.title }}</h3>
                <div class="badges">
                  <span class="pill" [class]="pillClass(campaign)">{{ stage(campaign) }}</span>
                </div>
                <div class="actions">
                  <a class="btn small secondary" [routerLink]="['/voting', campaign.id]">
                    {{ campaign.status === 'Published' ? 'View result' : 'View' }}
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
export class CampaignsListComponent implements OnInit {
  private readonly api = inject(VotingApi);

  readonly campaigns = signal<readonly Campaign[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Anything a member can still act on, or is waiting on a result from. */
  readonly current = computed(() =>
    this.campaigns().filter((campaign) => campaign.status !== 'Published'),
  );

  readonly past = computed(() =>
    this.campaigns().filter((campaign) => campaign.status === 'Published'),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.list().subscribe({
      next: (found) => {
        this.campaigns.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  stage(campaign: Campaign): string {
    return stageLabel(campaign);
  }

  pillClass(campaign: Campaign): string {
    if (campaign.status === 'Published') {
      return 'ok';
    }

    return campaign.acceptsVotes || campaign.acceptsNominations ? 'warn' : '';
  }

  /**
   * What a member can do about this campaign, and by when.
   *
   * Built from `acceptsVotes`/`acceptsNominations` rather than the status, so a
   * campaign whose window has passed does not still say "open".
   */
  describe(campaign: Campaign): string {
    if (campaign.acceptsNominations) {
      return `Nominations close ${this.date(campaign.nominationEndAt)}.`;
    }

    if (campaign.acceptsVotes) {
      return `Voting closes ${this.date(campaign.votingEndAt)}. One vote each.`;
    }

    switch (campaign.status) {
      case 'Draft':
        return 'Not open yet.';
      case 'NominationsOpen':
        return 'Nominations have closed. Voting has not opened.';
      case 'VotingOpen':
        return 'Voting has closed. The result is being counted.';
      case 'Closed':
        return `Voting is over. The top ${campaign.topN} will be announced.`;
      case 'Published':
        return `The top ${campaign.topN} have been announced.`;
    }
  }

  action(campaign: Campaign): string {
    if (campaign.acceptsVotes) {
      return campaign.myVoteCandidateId === null ? 'Vote' : 'View the ballot';
    }

    return campaign.acceptsNominations ? 'Nominate' : 'View';
  }

  private date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
