import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MatrimonyApi } from './matrimony.api';
import { Proposal } from './matrimony.models';

type ProposalTab = 'sent' | 'received';

/**
 * Proposals, `/matrimony/proposals` - Sent/Received tabs, the same
 * `role="tablist"` pattern My Profile and admin-portal's Master Data screen
 * already use. Both lists are loaded once on init rather than reloaded per
 * tab switch - cheap, unpaginated reads, unlike Master Data's per-tab fetch.
 */
@Component({
  selector: 'app-matrimony-proposals',
  imports: [RouterLink, AuthedImageDirective],
  styleUrl: './matrimony.css',
  template: `
    <div class="matrimony-page">
      <a class="back" routerLink="/matrimony">‹ Back to My Biodata</a>

      <header class="page-head">
        <div>
          <h1 class="page-title">My proposals</h1>
        </div>
      </header>

      <div class="tabs" role="tablist" aria-label="Proposals">
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'sent'"
          [attr.aria-selected]="activeTab() === 'sent'"
          (click)="activeTab.set('sent')"
        >
          Sent
        </button>
        <button
          type="button"
          role="tab"
          [class.on]="activeTab() === 'received'"
          [attr.aria-selected]="activeTab() === 'received'"
          (click)="activeTab.set('received')"
        >
          Received
        </button>
      </div>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        @if (actionError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        @if (activeTab() === 'sent') {
          @if (sent().length === 0) {
            <p class="notice info" role="status">You have not sent any proposals.</p>
          } @else {
            <div class="queue">
              @for (proposal of sent(); track proposal.id) {
                <div class="card">
                  @if (proposal.otherParty?.photoUrl; as path) {
                    <img class="candidate-photo small" [scAuthedSrc]="path" alt="" />
                  }
                  <p>
                    <b>{{ otherPartyName(proposal) }}</b>
                    <span class="pill" [class]="statusClass(proposal)">{{ proposal.status }}</span>
                  </p>
                  @if (proposal.message; as message) {
                    <p class="group-body">{{ message }}</p>
                  }
                  @if (proposal.otherParty; as otherParty) {
                    <div class="actions">
                      <a class="btn small secondary" [routerLink]="['/matrimony/candidates', otherParty.memberId]">
                        View biodata
                      </a>
                    </div>
                  }
                  @if (proposal.status === 'Pending') {
                    <div class="actions">
                      <button
                        class="btn small secondary"
                        type="button"
                        [disabled]="busy()"
                        (click)="withdraw(proposal.id)"
                      >
                        Withdraw
                      </button>
                    </div>
                  }
                </div>
              }
            </div>
          }
        } @else {
          @if (received().length === 0) {
            <p class="notice info" role="status">You have not received any proposals.</p>
          } @else {
            <div class="queue">
              @for (proposal of received(); track proposal.id) {
                <div class="card">
                  @if (proposal.otherParty?.photoUrl; as path) {
                    <img class="candidate-photo small" [scAuthedSrc]="path" alt="" />
                  }
                  <p>
                    <b>{{ otherPartyName(proposal) }}</b>
                    <span class="pill" [class]="statusClass(proposal)">{{ proposal.status }}</span>
                  </p>
                  @if (proposal.message; as message) {
                    <p class="group-body">{{ message }}</p>
                  }
                  @if (proposal.otherParty; as otherParty) {
                    <div class="actions">
                      <a class="btn small secondary" [routerLink]="['/matrimony/candidates', otherParty.memberId]">
                        View biodata
                      </a>
                    </div>
                  }
                  @if (proposal.status === 'Pending') {
                    <div class="actions">
                      <button class="btn small" type="button" [disabled]="busy()" (click)="respond(proposal.id, true)">
                        Accept
                      </button>
                      <button
                        class="btn small secondary"
                        type="button"
                        [disabled]="busy()"
                        (click)="respond(proposal.id, false)"
                      >
                        Decline
                      </button>
                    </div>
                  }
                </div>
              }
            </div>
          }
        }
      }
    </div>
  `,
})
export class ProposalsComponent implements OnInit {
  private readonly api = inject(MatrimonyApi);

  readonly activeTab = signal<ProposalTab>('sent');

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly sent = signal<readonly Proposal[]>([]);
  readonly received = signal<readonly Proposal[]>([]);

  readonly busy = signal(false);
  readonly actionError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.sent().subscribe({
      next: (found) => {
        this.sent.set(found);
        this.loadReceived();
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadReceived(): void {
    this.api.received().subscribe({
      next: (found) => {
        this.received.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  respond(proposalId: string, accept: boolean): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.api.respond(proposalId, accept).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(failure));
      },
    });
  }

  withdraw(proposalId: string): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.api.withdrawProposal(proposalId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(failure));
      },
    });
  }

  /** The other party's name, or a plain statement when their account is gone. */
  otherPartyName(proposal: Proposal): string {
    return proposal.otherParty?.fullName ?? 'This member is no longer on the platform';
  }

  statusClass(proposal: Proposal): string {
    switch (proposal.status) {
      case 'Accepted':
        return 'ok';
      case 'Pending':
        return 'warn';
      case 'Declined':
      case 'Withdrawn':
        return 'danger';
    }
  }
}
