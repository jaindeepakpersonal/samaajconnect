import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { forkJoin } from 'rxjs';
import { PrivacyApi } from './privacy.api';
import {
  ConsentNotice,
  ConsentNoticeItem,
  ConsentState,
  EraseResult,
  IdentityExport,
} from './privacy.models';

/**
 * Your data and privacy - the member's own DPDP rights, in one place.
 *
 * No wireframe covers this screen. The prototype has a `#profile` screen with
 * per-field directory privacy, which is a different thing: who in the Samaaj
 * can see your address is a directory setting, while withdrawing consent,
 * obtaining a copy and erasing an account are rights under the Act that exist
 * whatever the directory is set to. The endpoints for all three have existed
 * and been tested end to end for some time, but **a right reachable only with
 * curl is not one a member has**, which is what this screen is for.
 *
 * The three sections are the three rights, in the order of how drastic they
 * are.
 *
 * **Withdrawing has no confirmation step.** Section 6(4) requires withdrawing
 * to be as easy as giving, and giving was a tick during registration. Putting
 * an "are you sure?" in front of it would make it harder than giving, which is
 * the thing the section is there to prevent. The required purpose is the
 * exception, and it is not a button at all: it explains that erasing the
 * account is what withdrawing membership means.
 *
 * **Erasing has one, and a password.** Not to discourage it - section 12 is a
 * right, not a request - but because it cannot be undone and because a
 * Fiduciary should know who it is acting for before acting irreversibly.
 *
 * **What is kept is shown, not buried.** A member told only "done" has no way
 * to know an audit record survives. Section 8(7) allows retention required by
 * other law, so saying what is kept and why is part of honouring the right.
 */
@Component({
  selector: 'app-privacy',
  imports: [FormsModule, RouterLink],
  styleUrl: './privacy.css',
  template: `
    <div class="privacy-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Your data and privacy</h1>
          <p class="subtitle">
            What you agreed to, a copy of what is held, and how to have it erased.
          </p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (erased(); as done) {
        <!-- The account is gone. Nothing else on this screen still applies, so
             nothing else is shown. -->
        <h2 class="section-heading">Your account has been erased</h2>

        <p class="notice info" role="status">
          Erased {{ dateTime(done.erasedAt) }}.
        </p>

        <div class="grid2">
          <div class="card">
            <h2>What was erased</h2>
            <ul>
              @for (item of done.whatWasErased; track item) {
                <li>{{ item }}</li>
              }
            </ul>
          </div>

          <div class="card">
            <h2>What is kept, and why</h2>
            <ul>
              @for (item of done.whatIsKeptAndWhy; track item) {
                <li>{{ item }}</li>
              }
            </ul>
          </div>
        </div>

        <div class="actions">
          <button class="btn" type="button" (click)="finish()">Close and sign out</button>
        </div>
      } @else if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        <!-- 1. Consent ------------------------------------------------------ -->
        <h2 class="section-heading">What you agreed to</h2>

        @if (notice(); as shown) {
          <p class="small">
            Notice version {{ shown.version }}. Withdrawing takes effect immediately.
          </p>
        }

        <div class="grid">
          @for (item of purposes(); track item.purpose) {
            <div class="card">
              <h2>{{ item.title }}</h2>
              <p class="small">{{ item.description }}</p>

              <div class="badges">
                @if (isGranted(item)) {
                  <span class="pill ok">Agreed</span>
                } @else {
                  <span class="pill">Withdrawn</span>
                }

                @if (item.required) {
                  <span class="pill warn">Required</span>
                }
              </div>

              @if (decidedOn(item); as decided) {
                <p class="small">Last changed {{ date(decided) }}</p>
              }

              @if (consentError()[item.purpose]; as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              <div class="actions">
                @if (item.required) {
                  <!-- Not a disabled button: withdrawing this is not a thing
                       that is temporarily unavailable, it is a thing that means
                       something else. -->
                  <span class="small">
                    The account cannot exist without this. Erasing your account, below, is how
                    you withdraw it.
                  </span>
                } @else if (isGranted(item)) {
                  <button
                    class="btn secondary"
                    type="button"
                    [disabled]="busy()"
                    (click)="withdraw(item)"
                  >
                    Withdraw
                  </button>
                } @else {
                  <span class="small">
                    You have withdrawn this. Ask your Samaaj administrator if you want it back.
                  </span>
                }
              </div>
            </div>
          }
        </div>

        <!-- 2. A copy -------------------------------------------------------- -->
        <h2 class="section-heading">Get a copy of your data</h2>

        <p class="small">
          Everything the platform holds about you, as one file. Your data sits in three
          separate services, so the copy is put together in your browser from all three.
        </p>

        @if (identity(); as held)  {
          <div class="card">
            <h2>What is done with it</h2>
            <ul>
              @for (purpose of held.processingPurposes; track purpose.purpose) {
                <li><b>{{ purpose.title }}</b> — {{ purpose.description }}</li>
              }
            </ul>

            @if (held.heldElsewhere.length > 0) {
              <!-- A list, not a joined sentence: each entry names a service and
                   what it holds, and those phrases contain their own commas -
                   "your profile, family and children" - so joining them reads
                   as one unparseable run-on. -->
              <h3 class="section-heading">Also held elsewhere</h3>
              <ul>
                @for (place of held.heldElsewhere; track place) {
                  <li>{{ place }}</li>
                }
              </ul>
              <p class="small">The download includes all of these.</p>
            }
          </div>
        }

        @if (exportError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        @if (exportReady()) {
          <p class="notice info" role="status">
            Your copy has been downloaded.
          </p>
        }

        <div class="actions">
          <button class="btn" type="button" [disabled]="busy()" (click)="download()">
            {{ busy() ? 'Preparing…' : 'Download my data' }}
          </button>
        </div>

        <!-- 3. Erasure ------------------------------------------------------- -->
        <h2 class="section-heading">Erase your account</h2>

        <p class="small">
          This removes your profile, your household link, and any child you added on your own
          parental consent. It cannot be undone, and no administrator has to approve it.
        </p>

        @if (!confirming()) {
          <div class="actions">
            <button class="btn danger" type="button" (click)="askToConfirm()">
              Erase my account
            </button>
          </div>
        } @else {
          <div class="card">
            <h2>Confirm with your password</h2>
            <p class="small">
              We ask so that an irreversible action is a deliberate one, and so that it is
              you making it.
            </p>

            <label for="erase-password">Your password</label>
            <input
              class="input"
              id="erase-password"
              name="erase-password"
              type="password"
              autocomplete="current-password"
              [(ngModel)]="password"
              [attr.aria-invalid]="eraseError() !== null"
            />

            @if (eraseError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              <button
                class="btn danger"
                type="button"
                [disabled]="busy() || password.length === 0"
                (click)="erase()"
              >
                {{ busy() ? 'Erasing…' : 'Erase my account permanently' }}
              </button>
              <button class="btn secondary" type="button" (click)="cancelConfirm()">
                Cancel
              </button>
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class PrivacyComponent implements OnInit {
  private readonly api = inject(PrivacyApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly notice = signal<ConsentNotice | null>(null);
  readonly identity = signal<IdentityExport | null>(null);
  readonly consents = signal<readonly ConsentState[]>([]);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  readonly consentError = signal<Record<string, string>>({});

  readonly exportError = signal<string | null>(null);
  readonly exportReady = signal(false);

  readonly confirming = signal(false);
  readonly eraseError = signal<string | null>(null);
  readonly erased = signal<EraseResult | null>(null);

  password = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      notice: this.api.notice(),
      identity: this.api.identityExport(),
    }).subscribe({
      next: ({ notice, identity }) => {
        this.notice.set(notice);
        this.identity.set(identity);
        this.consents.set(identity.currentConsents);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  // ---- Consent -----------------------------------------------------------

  withdraw(item: ConsentNoticeItem): void {
    this.busy.set(true);

    const { [item.purpose]: _cleared, ...rest } = this.consentError();
    this.consentError.set(rest);

    this.api.withdraw(item.purpose).subscribe({
      next: (states) => {
        // The command answers with where every purpose now stands, so the
        // screen takes that rather than patching the one it changed.
        this.consents.set(states);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        this.consentError.set({
          ...this.consentError(),
          [item.purpose]: describeError(failure),
        });
        this.busy.set(false);
      },
    });
  }

  /** The notice's purposes, which is the list a member was actually shown. */
  purposes(): readonly ConsentNoticeItem[] {
    return this.notice()?.items ?? [];
  }

  isGranted(item: ConsentNoticeItem): boolean {
    return this.consents().find((state) => state.purpose === item.purpose)?.granted ?? false;
  }

  decidedOn(item: ConsentNoticeItem): string | null {
    return this.consents().find((state) => state.purpose === item.purpose)?.decidedAt ?? null;
  }

  // ---- A copy ------------------------------------------------------------

  /**
   * Assembles the copy and hands it to the browser.
   *
   * A blob rather than a link to an endpoint: there is no one endpoint to link
   * to, and three authenticated GETs cannot be expressed as an anchor anyway.
   * The object URL is revoked immediately - the download has already been
   * handed off by then, and leaving it would pin the whole export in memory.
   */
  download(): void {
    this.busy.set(true);
    this.exportError.set(null);
    this.exportReady.set(false);

    this.api.fullExport().subscribe({
      next: (data) => {
        this.busy.set(false);

        // Gathering the data and handing it to the browser are separate
        // failures and the member needs them told apart: the second one means
        // the copy exists and could not be saved, which is worth an explicit
        // message rather than a button that appears to do nothing.
        if (this.offer(data, `samaajconnect-my-data-${data.exportedAt.slice(0, 10)}.json`)) {
          this.exportReady.set(true);
        } else {
          this.exportError.set(
            'Your data was gathered but your browser would not save the file. ' +
              'Check whether downloads are blocked for this site, and try again.',
          );
        }
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.exportError.set(describeError(failure));
      },
    });
  }

  /**
   * Hands a JSON file to the browser. False when the browser would not take it.
   *
   * A blob rather than a link to an endpoint: there is no one endpoint to link
   * to, and three authenticated GETs cannot be expressed as an anchor anyway.
   * The object URL is revoked immediately - the download has been handed off by
   * then, and leaving it would pin the whole export in memory for the life of
   * the document.
   */
  private offer(data: unknown, filename: string): boolean {
    let url: string | null = null;

    try {
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });

      url = URL.createObjectURL(blob);

      const anchor = document.createElement('a');

      anchor.href = url;
      anchor.download = filename;
      anchor.click();

      return true;
    } catch {
      return false;
    } finally {
      if (url !== null) {
        URL.revokeObjectURL(url);
      }
    }
  }

  // ---- Erasure -----------------------------------------------------------

  askToConfirm(): void {
    this.confirming.set(true);
    this.eraseError.set(null);
    this.password = '';
  }

  cancelConfirm(): void {
    this.confirming.set(false);
    this.eraseError.set(null);
    this.password = '';
  }

  erase(): void {
    if (this.password.length === 0) {
      return;
    }

    this.busy.set(true);
    this.eraseError.set(null);

    this.api.erase(this.password).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.password = '';
        this.erased.set(result);
      },
      error: (failure: unknown) => {
        // A wrong password answers 403 Auth.StepUpFailed, which the auth
        // interceptor deliberately leaves alone. The panel stays open with the
        // message in it rather than the member being bounced to Login and left
        // wondering whether the erasure went through.
        this.busy.set(false);
        this.password = '';
        this.eraseError.set(describeError(failure));
      },
    });
  }

  /** The account is gone, so the session is too. */
  finish(): void {
    this.auth.signOut().subscribe({
      next: () => void this.router.navigate(['/login']),
      error: () => void this.router.navigate(['/login']),
    });
  }

  // ---- Formatting --------------------------------------------------------

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  dateTime(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime()) ? '' : date.toLocaleString();
  }
}
