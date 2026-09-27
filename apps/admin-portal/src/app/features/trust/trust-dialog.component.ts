import { AfterViewInit, Component, ElementRef, OnInit, ViewChild, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { Trust } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';
import { modalKeydown } from '../../core/modal-keys';

/**
 * Setting a Trust up, or correcting its name, as a modal over the list - the
 * dialog shape Events uses (focus kept inside, Escape closes, the list returns
 * focus to whatever opened it).
 *
 * **The locality is chosen once.** A Trust is its locality's Trust: exactly one
 * per locality, and a published Boli there opens what its winner owes to this
 * Trust. So editing shows the locality and does not offer to move it -
 * trust-service would refuse, and a Samaaj with the wrong one stands it down
 * and sets up the right one.
 */
@Component({
  selector: 'app-trust-dialog',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="closed.emit()">
      <div
        class="card modal-card modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="trust-dialog-title"
        (click)="$event.stopPropagation()"
        (keydown)="onKeydown($event)"
      >
        <h2 id="trust-dialog-title">{{ trust() ? 'Edit ' + trust()!.name : 'Set up a Trust' }}</h2>

        @if (error(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        <form (ngSubmit)="save()">
          <label for="trust-name">Name</label>
          <input id="trust-name" class="input" name="name" [(ngModel)]="name" maxlength="200"
            placeholder="Hiran Magri Samaaj Trust" #firstField />

          <label for="trust-locality">Locality</label>
          @if (trust(); as existing) {
            <input id="trust-locality" class="input" [value]="localities.label(existing.localityId)" disabled />
            <p class="small">
              A Trust stays with the locality it was set up for - a Boli published there is
              owed to it. Stand this one down and set up another to change it.
            </p>
          } @else {
            <select id="trust-locality" class="input" name="locality" [(ngModel)]="localityId">
              <option value="">Choose a locality…</option>
              @for (option of localities.options(); track option.id) {
                <option [value]="option.id">{{ option.label }}</option>
              }
            </select>
            <p class="small">A locality holds exactly one Trust. A second one for it is refused.</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy() || !canSave()">
              {{ trust() ? 'Save' : 'Set up Trust' }}
            </button>
            <button class="btn alt" type="button" (click)="closed.emit()">Cancel</button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class TrustDialogComponent implements OnInit, AfterViewInit {
  private readonly api = inject(AdminApi);

  readonly localities = inject(LocalityOptions);

  /** The Trust being edited; absent to set one up. */
  readonly trust = input<Trust | null>(null);

  /** Emitted with a sentence saying what happened. */
  readonly saved = output<string>();
  readonly closed = output<void>();

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  name = '';
  localityId = '';

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  ngOnInit(): void {
    this.localities.load();
    this.name = this.trust()?.name ?? '';
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  canSave(): boolean {
    return this.name.trim().length > 0 && (this.trust() !== null || this.localityId.length > 0);
  }

  onKeydown(event: KeyboardEvent): void {
    modalKeydown(event, () => this.closed.emit());
  }

  save(): void {
    if (!this.canSave() || this.busy()) {
      return;
    }

    const name = this.name.trim();
    const existing = this.trust();
    const work = existing ? this.api.renameTrust(existing.id, name) : this.api.createTrust(this.localityId, name);

    this.busy.set(true);
    this.error.set(null);

    work.subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.emit(existing ? `${name} is saved.` : `${name} is set up.`);
      },
      error: (failure: unknown) => {
        // Kept in the dialog, with what was typed.
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
