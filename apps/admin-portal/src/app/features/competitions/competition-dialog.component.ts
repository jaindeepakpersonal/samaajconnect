import { AfterViewInit, Component, ElementRef, OnInit, ViewChild, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { Competition, CompetitionInput, City, LookupItem } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';
import { blankToNull, modalKeydown } from '../../core/modal-keys';

/**
 * Setting a competition up, or correcting one, as a modal over the list.
 *
 * **Editable until registration closes**, the same rule competition-service
 * applies (409 after): once registration has closed, members have signed up to
 * these details and rounds are being run. The list offers Edit only while the
 * service would take it.
 *
 * **The locality is optional** - "Whole Samaaj" is most competitions - and it
 * bounds nobody: any member may take part. It is what the locality filter reads.
 *
 * **The format is asked for here, up front (2026-10-25)**: the competition type
 * (master data's Competition type list), how many rounds, the max marks, and
 * whether an audition comes first - with its date and how many go through.
 * Nothing is judged until judging starts, which is when the rounds are made,
 * so all of it stays editable until registration closes.
 */
@Component({
  selector: 'app-competition-dialog',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="closed.emit()">
      <div
        class="card modal-card modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="competition-dialog-title"
        (click)="$event.stopPropagation()"
        (keydown)="onKeydown($event)"
      >
        <h2 id="competition-dialog-title">
          {{ competition() ? 'Edit ' + competition()!.title : 'Set up a competition' }}
        </h2>

        @if (error(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        <form (ngSubmit)="save()">
          <label for="competition-title">Title</label>
          <input id="competition-title" class="input" name="title" [(ngModel)]="title"
            maxlength="200" placeholder="Rangoli Contest 2026" #firstField />

          <label for="competition-type">Competition type</label>
          <select id="competition-type" class="input" name="type" [(ngModel)]="competitionTypeId">
            <option value="">Not specified</option>
            @for (type of types(); track type.id) {
              <option [value]="type.id">{{ type.label }}</option>
            }
          </select>

          <label for="competition-description">Description</label>
          <textarea id="competition-description" class="input" name="description" rows="3"
            [(ngModel)]="description" maxlength="2000"></textarea>

          <label for="competition-venue">Venue</label>
          <input id="competition-venue" class="input" name="venue" [(ngModel)]="venue"
            maxlength="300" placeholder="Mahavir Bhawan, Hall 2" />

          <label for="competition-city">City or village</label>
          <select id="competition-city" class="input" name="city" [(ngModel)]="cityId">
            <option value="">Not specified</option>
            @for (city of cities(); track city.id) {
              <option [value]="city.id">{{ city.name }}</option>
            }
          </select>

          <label for="competition-locality">Locality</label>
          <select id="competition-locality" class="input" name="locality" [(ngModel)]="localityId">
            <option value="">Whole Samaaj</option>
            @for (option of localities.options(); track option.id) {
              <option [value]="option.id">{{ option.label }}</option>
            }
          </select>

          <div class="form-grid">
            <div class="field">
              <label for="reg-start">Registration opens</label>
              <input id="reg-start" class="input" type="datetime-local" name="registrationStartAt"
                [(ngModel)]="registrationStartAt" />
            </div>
            <div class="field">
              <label for="reg-end">Registration closes</label>
              <input id="reg-end" class="input" type="datetime-local" name="registrationEndAt"
                [(ngModel)]="registrationEndAt" [attr.aria-invalid]="windowInvalid() ? 'true' : null" />
            </div>
          </div>

          @if (windowInvalid()) {
            <p class="notice warn" role="alert">Registration cannot close before it opens.</p>
          }

          <h3>How it is judged</h3>
          <div class="form-grid">
            <div class="field">
              <label for="competition-rounds">Number of rounds</label>
              <input id="competition-rounds" class="input" type="number" name="roundCount" min="0" max="20"
                [(ngModel)]="roundCount" [attr.aria-invalid]="roundsInvalid() ? 'true' : null" />
            </div>
            <div class="field">
              <label for="competition-max">Max marks</label>
              <input id="competition-max" class="input" type="number" name="maxMarks" min="1" max="1000"
                [(ngModel)]="maxMarks" [attr.aria-invalid]="marksInvalid() ? 'true' : null" />
            </div>
          </div>
          <p class="small">
            @if (roundCount === 0) {
              No rounds: the judges mark everybody once, and members see the top three.
            } @else {
              Every participant takes part in all {{ roundCount }} round(s); the winners have the
              highest total of their round averages.
            }
            Each judge marks out of {{ maxMarks || '…' }}.
          </p>

          <label class="inline-check">
            <input type="checkbox" name="auditionRequired" [(ngModel)]="auditionRequired" />
            An audition comes first
          </label>

          @if (auditionRequired) {
            <div class="form-grid">
              <div class="field">
                <label for="competition-audition-date">Audition date</label>
                <input id="competition-audition-date" class="input" type="date" name="auditionDate"
                  [(ngModel)]="auditionDate" [attr.aria-invalid]="auditionInvalid() ? 'true' : null" />
              </div>
              <div class="field">
                <label for="competition-finalists">How many go through</label>
                <input id="competition-finalists" class="input" type="number" name="finalistCount" min="1" max="500"
                  [(ngModel)]="finalistCount" [attr.aria-invalid]="auditionInvalid() ? 'true' : null" />
              </div>
            </div>
            <p class="small">
              The audition judges mark everybody who applied; the top {{ finalistCount || 'N' }} by
              average go through to every round - and anybody tied with the last of them.
            </p>
          }

          @if (formatProblem(); as problem) {
            <p class="notice warn" role="alert">{{ problem }}</p>
          }

          @if (!competition()) {
            <p class="small">It starts as a draft: registration does not open until you move it on.</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy() || !canSave()">
              {{ competition() ? 'Save' : 'Create as a draft' }}
            </button>
            <button class="btn alt" type="button" (click)="closed.emit()">Cancel</button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: `
    .inline-check {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: var(--space-3) 0;
    }
  `,
})
export class CompetitionDialogComponent implements OnInit, AfterViewInit {
  private readonly api = inject(AdminApi);

  readonly localities = inject(LocalityOptions);

  /** The competition being edited; absent to set one up. */
  readonly competition = input<Competition | null>(null);

  readonly saved = output<string>();
  readonly closed = output<void>();

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  title = '';
  description = '';
  localityId = '';
  venue = '';
  cityId = '';
  registrationStartAt = '';

  /** Every active city or village, for where it is held. */
  readonly cities = signal<readonly City[]>([]);
  registrationEndAt = '';

  /** Master data's Competition type list. */
  readonly types = signal<readonly LookupItem[]>([]);

  competitionTypeId = '';
  roundCount = 0;
  maxMarks = 100;
  auditionRequired = false;
  auditionDate = '';
  finalistCount: number | null = null;

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  ngOnInit(): void {
    this.localities.load();
    this.api.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.api.listLookupItems('CompetitionType').subscribe({ next: (found) => this.types.set(found), error: () => {} });

    const existing = this.competition();

    if (existing) {
      this.title = existing.title;
      this.description = existing.description ?? '';
      this.localityId = existing.localityId ?? '';
      this.venue = existing.venue ?? '';
      this.cityId = existing.cityId ?? '';
      this.registrationStartAt = toLocalInput(existing.registrationStartAt);
      this.registrationEndAt = toLocalInput(existing.registrationEndAt);
      this.competitionTypeId = existing.competitionTypeId ?? '';
      this.roundCount = existing.roundCount;
      this.maxMarks = existing.maxMarks;
      this.auditionRequired = existing.auditionRequired;
      this.auditionDate = existing.auditionDate ?? '';
      this.finalistCount = existing.finalistCount;
    }
  }

  roundsInvalid(): boolean {
    return !Number.isInteger(this.roundCount) || this.roundCount < 0 || this.roundCount > 20;
  }

  marksInvalid(): boolean {
    return !Number.isInteger(this.maxMarks) || this.maxMarks < 1 || this.maxMarks > 1000;
  }

  auditionInvalid(): boolean {
    if (!this.auditionRequired) {
      return false;
    }

    const count = this.finalistCount;

    return !this.auditionDate || count === null || !Number.isInteger(count) || count < 1 || count > 500;
  }

  /** The same rules competition-service applies, said before a round trip rather than after. */
  formatProblem(): string | null {
    if (this.roundsInvalid()) {
      return 'Rounds must be a whole number from 0 to 20.';
    }

    if (this.marksInvalid()) {
      return 'Max marks must be a whole number from 1 to 1000.';
    }

    if (this.auditionInvalid()) {
      return 'Give the audition date, and how many go through - from 1 to 500.';
    }

    if (this.auditionRequired && this.registrationStartAt && this.auditionDate < this.registrationStartAt.slice(0, 10)) {
      return 'The audition cannot be before registration opens.';
    }

    return null;
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  windowInvalid(): boolean {
    return (
      this.registrationStartAt.length > 0 &&
      this.registrationEndAt.length > 0 &&
      new Date(this.registrationEndAt) <= new Date(this.registrationStartAt)
    );
  }

  canSave(): boolean {
    return (
      this.title.trim().length > 0 &&
      this.registrationStartAt.length > 0 &&
      this.registrationEndAt.length > 0 &&
      !this.windowInvalid() &&
      this.formatProblem() === null
    );
  }

  onKeydown(event: KeyboardEvent): void {
    modalKeydown(event, () => this.closed.emit());
  }

  save(): void {
    if (!this.canSave() || this.busy()) {
      return;
    }

    const existing = this.competition();
    const input: CompetitionInput = {
      title: this.title.trim(),
      description: blankToNull(this.description),
      competitionTypeId: this.competitionTypeId || null,
      localityId: this.localityId || null,
      venue: blankToNull(this.venue),
      cityId: this.cityId || null,
      registrationStartAt: new Date(this.registrationStartAt).toISOString(),
      registrationEndAt: new Date(this.registrationEndAt).toISOString(),
      roundCount: this.roundCount,
      maxMarks: this.maxMarks,
      auditionRequired: this.auditionRequired,
      auditionDate: this.auditionRequired ? this.auditionDate : null,
      finalistCount: this.auditionRequired ? this.finalistCount : null,
    };

    const work = existing ? this.api.updateCompetition(existing.id, input) : this.api.createCompetition(input);

    this.busy.set(true);
    this.error.set(null);

    work.subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.emit(
          existing
            ? `${input.title} is saved.`
            : `${input.title} created as a draft. Registration is not open yet.`,
        );
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}

/** An ISO instant as a datetime-local value in the viewer's own time zone. */
function toLocalInput(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => `${n}`.padStart(2, '0');

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
