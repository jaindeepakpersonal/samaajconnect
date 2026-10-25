import { AfterViewInit, Component, ElementRef, OnInit, ViewChild, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { City, Occasion } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';
import { blankToNull, modalKeydown } from '../../core/modal-keys';

/**
 * Announcing a Boli occasion, or correcting one, as a modal over the list.
 *
 * **The locality matters here in a way it does not elsewhere**: a Boli won
 * under an occasion in a locality becomes an amount its winner owes that
 * locality's Trust when the result is announced. So it may be corrected until
 * the occasion closes (boli-service's rule) and not after, when results have
 * been announced against it.
 */
@Component({
  selector: 'app-occasion-dialog',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="closed.emit()">
      <div
        class="card modal-card modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="occasion-dialog-title"
        (click)="$event.stopPropagation()"
        (keydown)="onKeydown($event)"
      >
        <h2 id="occasion-dialog-title">
          {{ occasion() ? 'Edit ' + occasion()!.title : 'Announce an occasion' }}
        </h2>

        @if (error(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        <form (ngSubmit)="save()">
          <label for="occasion-title">Title</label>
          <input id="occasion-title" class="input" name="title" [(ngModel)]="title"
            maxlength="200" placeholder="Paryushan 2026" #firstField />

          <label for="occasion-description">Description</label>
          <textarea id="occasion-description" class="input" name="description" rows="3"
            [(ngModel)]="description" maxlength="2000"></textarea>

          <label for="occasion-date">Date</label>
          <input id="occasion-date" class="input inline" type="date" name="occasionDate"
            [(ngModel)]="occasionDate" />

          <label for="occasion-venue">Venue</label>
          <input id="occasion-venue" class="input" name="venue" [(ngModel)]="venue"
            maxlength="300" placeholder="Shri Mahavir Mandir" />

          <label for="occasion-city">City or village</label>
          <select id="occasion-city" class="input" name="city" [(ngModel)]="cityId">
            <option value="">Not specified</option>
            @for (city of cities(); track city.id) {
              <option [value]="city.id">{{ city.name }}</option>
            }
          </select>

          <label for="occasion-locality">Locality</label>
          <select id="occasion-locality" class="input" name="locality" [(ngModel)]="localityId">
            <option value="">Whole Samaaj</option>
            @for (option of localities.options(); track option.id) {
              <option [value]="option.id">{{ option.label }}</option>
            }
          </select>
          <p class="small">
            A Boli won under an occasion in a locality is owed to that locality's Trust once its
            result is announced.
          </p>

          @if (!occasion()) {
            <p class="small">It starts Upcoming. Nothing under it takes bids until you make it active.</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy() || !canSave()">
              {{ occasion() ? 'Save' : 'Announce occasion' }}
            </button>
            <button class="btn alt" type="button" (click)="closed.emit()">Cancel</button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class OccasionDialogComponent implements OnInit, AfterViewInit {
  private readonly api = inject(AdminApi);

  readonly localities = inject(LocalityOptions);

  /** The occasion being edited; absent to announce one. */
  readonly occasion = input<Occasion | null>(null);

  readonly saved = output<string>();
  readonly closed = output<void>();

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  title = '';
  description = '';
  occasionDate = '';
  localityId = '';
  venue = '';
  cityId = '';

  /** Every active city or village, for where it is held. */
  readonly cities = signal<readonly City[]>([]);

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  ngOnInit(): void {
    this.api.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.localities.load();

    const existing = this.occasion();

    if (existing) {
      this.title = existing.title;
      this.description = existing.description ?? '';
      this.occasionDate = existing.occasionDate;
      this.localityId = existing.localityId ?? '';
      this.venue = existing.venue ?? '';
      this.cityId = existing.cityId ?? '';
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  canSave(): boolean {
    return this.title.trim().length > 0 && this.occasionDate.length > 0;
  }

  onKeydown(event: KeyboardEvent): void {
    modalKeydown(event, () => this.closed.emit());
  }

  save(): void {
    if (!this.canSave() || this.busy()) {
      return;
    }

    const existing = this.occasion();
    const title = this.title.trim();
    const description = blankToNull(this.description);
    const localityId = this.localityId || null;

    const work = existing
      ? this.api.updateOccasion(
          existing.id, title, description, this.occasionDate, localityId, blankToNull(this.venue), this.cityId || null)
      : this.api.createOccasion(
          title, description, this.occasionDate, localityId, blankToNull(this.venue), this.cityId || null);

    this.busy.set(true);
    this.error.set(null);

    work.subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.emit(existing ? `${title} is saved.` : `${title} announced.`);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
