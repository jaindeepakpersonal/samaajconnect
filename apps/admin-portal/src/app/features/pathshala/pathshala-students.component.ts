import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { PathshalaClass, PathshalaStudent } from '../../core/admin.models';

/**
 * The children a Pathshala teaches, and the ones waiting for a place, each with
 * the parent to call.
 *
 * **The parent's contact is what they gave on the request**, not a lookup into
 * the member directory: a phone number there carries the member's own privacy
 * level, and a parent asking a Pathshala for a place is handing it the number
 * to call about their child. Requests made before the form asked have none, and
 * the screen says so instead of leaving a blank that reads as a lost value.
 *
 * **A teacher sees their own classes' children only** - the service narrows
 * the list - and never the placement queue, which is the administrators'.
 *
 * **Names come from `/v1/children/names`**, for exactly the ids on screen: the
 * child record itself carries a date of birth and the parental-consent record,
 * which a list printing a name has no business receiving.
 */
@Component({
  selector: 'app-pathshala-students',
  imports: [FormsModule, DatePipe, NgTemplateOutlet],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (canManage()) {
      <div class="card">
        <h2>Waiting for a place</h2>

        @if (waiting().length === 0) {
          <p class="empty">Nobody is waiting. Requests appear here when a parent asks for a place.</p>
        } @else {
          <p class="small">
            A parent asked; somebody here decides. Turning a request down does not delete the
            child's record.
          </p>

          <div class="table-wrap">
            <table>
              <caption class="sr-only">Children waiting for a place</caption>
              <thead>
                <tr><th>Child</th><th>Parent</th><th>Asked</th><th>Class</th><th></th></tr>
              </thead>
              <tbody>
                @for (request of waiting(); track request.enrolmentId) {
                  <tr>
                    <td><b>{{ childName(request.childProfileId) }}</b></td>
                    <td>
                      <ng-container *ngTemplateOutlet="contact; context: { $implicit: request }" />
                    </td>
                    <td>{{ request.requestedAt | date: 'd MMM y' }}</td>
                    <td>
                      @if (classes().length === 0) {
                        <span class="muted">No classes to place into</span>
                      } @else {
                        <label class="sr-only" [attr.for]="'class-' + request.enrolmentId">
                          Class for {{ childName(request.childProfileId) }}
                        </label>
                        <select
                          class="input inline"
                          [id]="'class-' + request.enrolmentId"
                          [name]="'class-' + request.enrolmentId"
                          [(ngModel)]="chosenClass[request.enrolmentId]"
                        >
                          <option value="">Choose a class…</option>
                          @for (klass of classes(); track klass.id) {
                            <option [value]="klass.id">{{ klass.name }}</option>
                          }
                        </select>
                      }
                    </td>
                    <td class="row-actions">
                      <button class="btn" type="button"
                        [disabled]="busy() || !chosenClass[request.enrolmentId]"
                        (click)="place(request, true)">
                        Place
                      </button>
                      <button class="btn alt" type="button" [disabled]="busy()"
                        (click)="place(request, false)">
                        Turn down
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }

    <div class="card spaced">
      <h2>Students</h2>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (enrolled().length === 0) {
        <p class="empty">Nobody is enrolled yet.</p>
      } @else {
        <label for="student-class">Class</label>
        <select id="student-class" class="input inline" name="studentClass"
          [ngModel]="classFilter()" (ngModelChange)="classFilter.set($event)">
          <option value="">Every class</option>
          @for (klass of classes(); track klass.id) {
            <option [value]="klass.id">{{ klass.name }}</option>
          }
        </select>

        <div class="table-wrap">
          <table>
            <caption class="sr-only">Children enrolled at this Pathshala</caption>
            <thead>
              <tr><th>Child</th><th>Class</th><th>Parent</th><th>Enrolled</th></tr>
            </thead>
            <tbody>
              @for (student of shown(); track student.enrolmentId) {
                <tr>
                  <td><b>{{ childName(student.childProfileId) }}</b></td>
                  <td>
                    {{ student.className ?? '—' }}
                    @if (student.sessionLabel; as label) { <span class="muted">· {{ label }}</span> }
                  </td>
                  <td>
                    <ng-container *ngTemplateOutlet="contact; context: { $implicit: student }" />
                  </td>
                  <td>{{ student.enrolledAt | date: 'd MMM y' }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="muted">Nobody in this class.</td></tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (left() > 0) {
        <p class="small">
          {{ left() }} more {{ left() === 1 ? 'child has' : 'children have' }} left or been turned
          down. Their records and attendance are kept.
        </p>
      }
    </div>

    <ng-template #contact let-row>
      @if (row.contactPhone) {
        {{ row.contactName ?? 'Parent' }} ·
        <a [href]="'tel:' + row.contactPhone">{{ row.contactPhone }}</a>
      } @else {
        <span class="muted">Not given - asked before the form did</span>
      }
    </ng-template>
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }
  `,
})
export class PathshalaStudentsComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly pathshalaId = input.required<string>();
  readonly classes = input<readonly PathshalaClass[]>([]);
  readonly canManage = input(false);

  /** Placing a child changes a class's count, so the parent re-reads it. */
  readonly changed = output<void>();

  readonly students = signal<readonly PathshalaStudent[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly classFilter = signal('');

  /** Child id → name. */
  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  chosenClass: Record<string, string> = {};

  readonly waiting = computed(() => this.students().filter((s) => s.status === 'Requested'));
  readonly enrolled = computed(() => this.students().filter((s) => s.status === 'Active'));
  readonly left = computed(
    () => this.students().filter((s) => s.status === 'Withdrawn' || s.status === 'Declined').length,
  );

  readonly shown = computed(() => {
    const classId = this.classFilter();

    return classId === '' ? this.enrolled() : this.enrolled().filter((s) => s.classId === classId);
  });

  ngOnInit(): void {
    this.load();
  }

  childName(childProfileId: string): string {
    // "A child" rather than a GUID: an id says less than admitting the name
    // could not be resolved.
    return this.names().get(childProfileId) ?? 'A child';
  }

  place(request: PathshalaStudent, place: boolean): void {
    const classId = place ? (this.chosenClass[request.enrolmentId] ?? '') : '';

    if (place && classId.length === 0) {
      return;
    }

    const who = this.childName(request.childProfileId);

    this.act(
      this.api.placeStudent(request.enrolmentId, place ? classId : null, place),
      place ? `${who} has a place.` : `${who}'s request was turned down.`,
    );
  }

  private load(): void {
    this.api.pathshalaStudents(this.pathshalaId()).subscribe({
      next: (found) => {
        this.students.set(found);
        this.loading.set(false);

        // Seeded to '' because an undefined bound to a <select> matches no
        // option, not even the placeholder.
        for (const student of found) {
          this.chosenClass[student.enrolmentId] ??= '';
        }

        this.loadNames(found.map((s) => s.childProfileId));
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  /** Names are a convenience, so a failure leaves "A child" rather than an error. */
  private loadNames(ids: readonly string[]): void {
    const distinct = [...new Set(ids)];

    if (distinct.length === 0) {
      return;
    }

    this.api.childNames(distinct).subscribe({
      next: (found) => this.names.set(new Map(found.map((c) => [c.id, c.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  private act(work: Observable<unknown>, message: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        this.load();
        this.changed.emit();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
