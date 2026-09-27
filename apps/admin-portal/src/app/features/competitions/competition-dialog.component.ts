import { AfterViewInit, Component, ElementRef, OnInit, ViewChild, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { Competition, CompetitionInput } from '../../core/admin.models';
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

          <label for="competition-description">Description</label>
          <textarea id="competition-description" class="input" name="description" rows="3"
            [(ngModel)]="description" maxlength="2000"></textarea>

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
            <p class="notice" role="alert">Registration cannot close before it opens.</p>
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
  registrationStartAt = '';
  registrationEndAt = '';

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  ngOnInit(): void {
    this.localities.load();

    const existing = this.competition();

    if (existing) {
      this.title = existing.title;
      this.description = existing.description ?? '';
      this.localityId = existing.localityId ?? '';
      this.registrationStartAt = toLocalInput(existing.registrationStartAt);
      this.registrationEndAt = toLocalInput(existing.registrationEndAt);
    }
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
      !this.windowInvalid()
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
      competitionTypeId: existing?.competitionTypeId ?? null,
      localityId: this.localityId || null,
      registrationStartAt: new Date(this.registrationStartAt).toISOString(),
      registrationEndAt: new Date(this.registrationEndAt).toISOString(),
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
