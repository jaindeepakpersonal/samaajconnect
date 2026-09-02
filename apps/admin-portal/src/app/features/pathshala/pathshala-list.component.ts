import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Pathshala } from '../../core/admin.models';

/**
 * Jain Pathshala, from the admin wireframe's `#pathshala` screen.
 *
 * **Running a Pathshala was a curl-only activity.** Thirteen of the twenty-seven
 * endpoints `scripts/unreachable-endpoints.sh` found were this module: a parent
 * could ask for a place and nobody could open a session, create a class, place a
 * child, mark a register or record an exam. This screen and the detail beside it
 * cover the first half — setting the Pathshala up, and answering the parents.
 *
 * **Creating a Pathshala is not here.** `CreatePathshalaCommand` is Super Admin
 * only (`DATA-MODEL.md` §9): the master record is the platform operator's, and
 * everything about *running* it is the Samaaj's. A create form on a Samaaj
 * administrator's screen would be a control that always answers 403.
 */
@Component({
  selector: 'app-pathshala-list',
  imports: [RouterLink],
  template: `
    <h1 class="title">Jain Pathshala</h1>
    <p class="sub">Sessions, classes and the children waiting for a place in {{ scope.label() }}.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
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
        This Samaaj has no Pathshala yet. Only the platform operator can create the master
        record; ask them to add one.
      </p>
    } @else {
      <div class="grid">
        @for (item of pathshalas(); track item.id) {
          <div class="card">
            <h3>{{ item.name }}</h3>

            @if (item.address) {
              <p class="muted">{{ item.address }}</p>
            }

            <p>
              @if (item.currentSessionLabel) {
                <span class="pill ok">{{ item.currentSessionLabel }}</span>
              } @else {
                <span class="pill warn">No session open</span>
              }
              @if (!item.acceptsEnrolments) {
                <span class="pill off">Not taking enrolments</span>
              }
            </p>

            <!--
              Counts, not rosters. The service answers with three numbers it can
              produce without sending anybody a list of children.
            -->
            <p class="muted">
              {{ item.classCount }} {{ item.classCount === 1 ? 'class' : 'classes' }} ·
              {{ item.teacherCount }} {{ item.teacherCount === 1 ? 'teacher' : 'teachers' }}
            </p>

            <div class="actions">
              <a class="btn" [routerLink]="['/pathshala', item.id]">Open</a>
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: var(--space-4);
    }
  `,
})
export class PathshalaListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

  readonly pathshalas = signal<readonly Pathshala[]>([]);
  readonly loading = signal(true);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  ngOnInit(): void {
    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.api.listPathshalas().subscribe({
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

export function isNotFound(failure: unknown): boolean {
  return typeof failure === 'object' && failure !== null && 'status' in failure
    && (failure as { status: unknown }).status === 404;
}
