import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope } from '../../core/area-scope';
import { isNotFound } from '../../core/http-status';
import { Pathshala } from '../../core/admin.models';
import { PathshalaDialogComponent } from './pathshala-dialog.component';

/**
 * Jain Pathshala, from the admin wireframe's `#pathshala` screen: every
 * Pathshala as a tile, with its create, edit and stand-down controls on the
 * tiles themselves.
 *
 * **Create and edit are one modal, as on the Events screen (2026-09-26).** The
 * form - name, a free-text address line plus the Country -> State -> City ->
 * Locality cascade, contact person - is `PathshalaDialogComponent`. Creating is
 * offered to a Super Admin scoped into a Samaaj and to nobody else:
 * `CreatePathshalaCommand` is Super Admin only (`DATA-MODEL.md` §9), because the
 * master record is the platform operator's. Editing is anybody's who runs the
 * Pathshala - `Pathshala.Manage`, and for an Area Admin only in their own
 * locality - because a wrong address is the Samaaj's to put right.
 *
 * **Standing one down moved onto its tile, and still asks first.** Records are
 * kept and enrolments stop, and it cannot be undone from this panel, so the
 * button opens a warning on the tile rather than firing - and stays in the DOM,
 * enabled, with `aria-expanded`, for the WCAG 2.4.3 reason every other
 * confirmation in this panel gives.
 */
@Component({
  selector: 'app-pathshala-list',
  imports: [RouterLink, PathshalaDialogComponent],
  template: `
    <div class="section-head">
      <div>
        <h1 class="title">Jain Pathshala</h1>
        <p class="sub">Sessions, classes, teachers and students in {{ scope.label() }}.</p>
      </div>

      @if (canCreate()) {
        <button class="btn" type="button" (click)="openCreate($event)">Create a Pathshala</button>
      }
    </div>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">A Pathshala belongs to a Samaaj. Choose one in the top bar.</p>
    } @else if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the Pathshala module. Switch it on from the Samaaj
        screen.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (pathshalas().length === 0) {
      <p class="empty">
        This Samaaj has no Pathshala yet.
        @if (!canCreate()) {
          Only the platform operator can create the master record; ask them to add one.
        }
      </p>
    } @else {
      <div class="grid">
        @for (item of pathshalas(); track item.id) {
          <div class="card">
            <h2>{{ item.name }}</h2>

            @if (item.address?.line; as line) {
              <p class="muted">{{ line }}</p>
            }

            <p>
              @if (item.status !== 'Active') {
                <span class="pill off">Stood down</span>
              } @else if (item.currentSessionLabel) {
                <span class="pill ok">{{ item.currentSessionLabel }}</span>
              } @else {
                <span class="pill warn">No session open</span>
              }
              @if (item.status === 'Active' && !item.acceptsEnrolments) {
                <span class="pill off">Not taking enrolments</span>
              }
            </p>

            <!--
              Counts, not rosters. The service answers with numbers it can
              produce without sending anybody a list of children.
            -->
            <p class="muted">
              {{ item.classCount }} {{ item.classCount === 1 ? 'class' : 'classes' }} ·
              {{ item.teacherCount }} {{ item.teacherCount === 1 ? 'teacher' : 'teachers' }}
            </p>

            <div class="actions">
              <a class="btn" [routerLink]="['/pathshala', item.id]">
                Open<span class="sr-only"> {{ item.name }}</span>
              </a>

              @if (canManage(item)) {
                <button class="btn alt" type="button" (click)="openEdit(item, $event)">
                  Edit<span class="sr-only"> {{ item.name }}</span>
                </button>

                @if (item.status === 'Active') {
                  <!--
                    The trigger stays, and stays enabled, while the warning is
                    open: replacing or disabling it drops a keyboard user's
                    focus to the body (WCAG 2.4.3).
                  -->
                  <button
                    class="btn alt"
                    type="button"
                    [attr.aria-expanded]="standingDown() === item.id"
                    (click)="standingDown.set(item.id)"
                  >
                    Stand down…<span class="sr-only"> {{ item.name }}</span>
                  </button>
                }
              }
            </div>

            @if (standingDown() === item.id) {
              <div class="notice spaced" role="status">
                <p>
                  <b>{{ item.name }}</b> will stop taking enrolments. Every session, class,
                  register and exam result is kept - a Pathshala that closed still taught the
                  children who attended it. This cannot be undone from this panel.
                </p>
                <div class="actions">
                  <button class="btn" type="button" [disabled]="busy()" (click)="standDown(item)">
                    Stand it down
                  </button>
                  <button class="btn alt" type="button" (click)="standingDown.set(null)">
                    Keep it running
                  </button>
                </div>
              </div>
            }
          </div>
        }
      </div>
    }

    @if (dialogOpen()) {
      <app-pathshala-dialog
        [pathshala]="editing()"
        [samaajLabel]="scope.label()"
        (saved)="onSaved($event)"
        (closed)="closeDialog()"
      />
    }
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: var(--space-4);
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .spaced {
      margin-top: var(--space-4);
    }
  `,
})
export class PathshalaListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly area = inject(AreaScope);

  readonly scope = inject(AdminScope);

  readonly pathshalas = signal<readonly Pathshala[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly dialogOpen = signal(false);
  readonly editing = signal<Pathshala | null>(null);
  private dialogTrigger: HTMLElement | null = null;

  /** Which tile's stand-down warning is open. */
  readonly standingDown = signal<string | null>(null);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  /**
   * Whether to offer creating one.
   *
   * The role *and* a chosen Samaaj: the command creates the record inside
   * whichever Samaaj the request is scoped to, so offering it with no scope
   * would be offering to create one nowhere in particular.
   */
  readonly canCreate = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() !== null,
  );

  private readonly managesPathshalas = computed(
    () => this.auth.user()?.permissions.includes('Pathshala.Manage') ?? false,
  );

  ngOnInit(): void {
    this.load();
  }

  /** Edit and stand down: `Pathshala.Manage`, and for an Area Admin only their own locality's. */
  canManage(item: Pathshala): boolean {
    return this.managesPathshalas() && this.area.administers(item.address?.localityId);
  }

  openCreate(event: MouseEvent): void {
    this.editing.set(null);
    this.openDialog(event);
  }

  openEdit(item: Pathshala, event: MouseEvent): void {
    this.editing.set(item);
    this.openDialog(event);
  }

  onSaved(message: string): void {
    this.closeDialog();
    this.done.set(message);
    this.load();
  }

  closeDialog(): void {
    this.dialogOpen.set(false);

    const trigger = this.dialogTrigger;
    this.dialogTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  standDown(item: Pathshala): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.deactivatePathshala(item.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.standingDown.set(null);
        this.done.set(`${item.name} has been stood down. Its records are kept.`);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  private openDialog(event: MouseEvent): void {
    this.dialogTrigger = event.currentTarget as HTMLElement;
    this.error.set(null);
    this.done.set(null);
    this.dialogOpen.set(true);
  }

  private load(): void {
    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    // Somebody who does not run Pathshalas is here to teach: they see the
    // ones they teach at, not the Samaaj's directory.
    this.api.listPathshalas(!this.managesPathshalas()).subscribe({
      next: (found) => {
        this.pathshalas.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        // 404 here is the module gate, not a missing route: the gateway answers
        // 404 for a Samaaj that has switched `pathshala` off, so that a Samaaj
        // without the module is indistinguishable from a platform with no such
        // feature.
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }
}
