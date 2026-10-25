import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MatrimonyApi } from './matrimony.api';
import { Biodata, Diet, DietLabels } from './matrimony.models';

/**
 * Candidate detail, `/matrimony/candidates/:id`.
 *
 * "Send proposal" is the substantial action here - an optional short message,
 * the same "toggle a form into view below a button" shape
 * `group-detail.component.ts` uses for applying to a group. A link back to the
 * existing Member detail screen is offered too, for anyone who wants the base
 * profile view as well.
 */
@Component({
  selector: 'app-candidate-detail',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
  styleUrl: './matrimony.css',
  template: `
    <div class="matrimony-page">
      <a class="back" routerLink="/matrimony/search">‹ Back to search</a>

      @if (loading()) {
        <p role="status">Loading candidate…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (candidate(); as found) {
        <h1 class="page-title">{{ found.fullName }}</h1>
        <p class="subtitle">
          {{ found.age }} years • {{ found.gender }}
          @if (found.heightCm; as height) {
            • {{ height }} cm
          }
        </p>

        @if (found.photoUrl; as path) {
          <img class="candidate-photo" [scAuthedSrc]="path" alt="" />
        }

        <div class="card">
          <h2>Biodata</h2>

          <dl class="details">
            @if (found.diet; as diet) {
              <dt>Diet</dt>
              <dd>{{ dietLabel(diet) }}</dd>
            }
            @if (found.familyType; as familyType) {
              <dt>Family type</dt>
              <dd>{{ familyType }}</dd>
            }
            @if (found.fatherOccupation; as value) {
              <dt>Father's occupation</dt>
              <dd>{{ value }}</dd>
            }
            @if (found.motherOccupation; as value) {
              <dt>Mother's occupation</dt>
              <dd>{{ value }}</dd>
            }
            @if (found.siblingsCount !== null) {
              <dt>Siblings</dt>
              <dd>{{ found.siblingsCount }}</dd>
            }
            @if (found.manglik !== null) {
              <dt>Manglik</dt>
              <dd>{{ found.manglik ? 'Yes' : 'No' }}</dd>
            }
            @if (found.preferredAgeMin !== null || found.preferredAgeMax !== null) {
              <dt>Preferred age</dt>
              <dd>{{ found.preferredAgeMin ?? '—' }} to {{ found.preferredAgeMax ?? '—' }}</dd>
            }
          </dl>

          @if (found.aboutMe; as about) {
            <h3>About</h3>
            <p class="group-body">{{ about }}</p>
          }

          @if (found.partnerPreferenceNotes; as notes) {
            <h3>Partner preference</h3>
            <p class="group-body">{{ notes }}</p>
          }

          @if (found.horoscopeNotes; as notes) {
            <h3>Horoscope notes</h3>
            <p class="group-body">{{ notes }}</p>
          }

          <div class="actions">
            <a class="btn small secondary" [routerLink]="['/members', found.memberId]">
              View base profile
            </a>
          </div>
        </div>

        @if (proposalError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }
        @if (proposalSent()) {
          <p class="notice ok" role="status">Your proposal has been sent.</p>
        } @else {
          <div class="card">
            <h2>Send a proposal</h2>

            @if (!showForm()) {
              <button class="btn" type="button" [disabled]="sending()" (click)="showForm.set(true)">
                Send proposal
              </button>
            } @else {
              <form (ngSubmit)="send(found.memberId)">
                <label for="message">Message (optional)</label>
                <textarea
                  class="input"
                  id="message"
                  name="message"
                  rows="3"
                  maxlength="500"
                  [(ngModel)]="message"
                ></textarea>

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="sending()">
                    {{ sending() ? 'Sending…' : 'Send' }}
                  </button>
                  <button class="btn secondary" type="button" (click)="showForm.set(false)">
                    Cancel
                  </button>
                </div>
              </form>
            }
          </div>
        }
      }
    </div>
  `,
})
export class CandidateDetailComponent implements OnInit {
  private readonly api = inject(MatrimonyApi);
  private readonly route = inject(ActivatedRoute);

  readonly candidate = signal<Biodata | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly showForm = signal(false);
  readonly sending = signal(false);
  readonly proposalError = signal<string | null>(null);
  readonly proposalSent = signal(false);

  message = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a candidate.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.candidate(id).subscribe({
      next: (found) => {
        this.candidate.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  send(toMemberId: string): void {
    this.sending.set(true);
    this.proposalError.set(null);

    const trimmed = this.message.trim();

    this.api.sendProposal(toMemberId, trimmed.length > 0 ? trimmed : null).subscribe({
      next: () => {
        this.sending.set(false);
        this.showForm.set(false);
        this.proposalSent.set(true);
      },
      error: (failure: unknown) => {
        this.sending.set(false);
        this.proposalError.set(describeError(failure));
      },
    });
  }

  dietLabel(diet: Diet): string {
    return DietLabels[diet];
  }
}
