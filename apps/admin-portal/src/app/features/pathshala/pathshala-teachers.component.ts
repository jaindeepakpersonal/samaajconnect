import { Component, ElementRef, OnInit, ViewChild, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { Observable, switchMap, tap } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import {
  AdminMember,
  PathshalaClass,
  PathshalaTeacher,
  TeacherInput,
} from '../../core/admin.models';
import { blankToNull, modalKeydown } from '../../core/modal-keys';

/** What pathshala-service accepts for a phone number, character for character. */
const PHONE = /^\+?[0-9 ()-]{7,20}$/;

/**
 * A Pathshala's teachers: who they are, how to reach them, what they teach,
 * and whether they can sign in.
 *
 * **A teacher is a record the Pathshala keeps, not a member.** Plenty of
 * Pathshalas are taught by somebody from outside the Samaaj, so the record
 * holds a name, a phone, an email and a qualification of its own, and a class
 * names its teachers by this record's id. That is what the class screen's
 * teacher picker offers.
 *
 * **A login is optional, and linking one is two acts on two services.** A
 * teacher who only teaches needs none. One who should mark registers and send
 * announcements needs an account holding `PathshalaTeacher`: an existing
 * member is given the role, or a new account is invited with it, and the
 * account's id is then written onto the record - which is what lets
 * pathshala-service recognise them. Only a Samaaj Admin or Super Admin may
 * grant a role, so only they are offered the button; an Area Admin keeps the
 * record and asks them.
 *
 * **Unlinking leaves the account alone.** The account may teach at another
 * Pathshala, and taking a role away is Admin Users' job, so the dialog says so
 * rather than doing it quietly.
 */
@Component({
  selector: 'app-pathshala-teachers',
  imports: [FormsModule],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <div class="head">
        <h2>Teachers</h2>

        @if (canManage()) {
          <button class="btn" type="button" (click)="openAdd($event)">Add a teacher</button>
        }
      </div>

      @if (removing(); as teacher) {
        <div class="notice" role="status">
          <p>
            <b>{{ teacher.fullName }}</b> will stop teaching every class here, and their record
            will be removed. Registers they marked and results they recorded are kept.
          </p>
          <div class="row-actions">
            <button class="btn" type="button" [disabled]="busy()" (click)="remove(teacher)">
              Remove them
            </button>
            <button class="btn alt" type="button" (click)="removing.set(null)">Keep them</button>
          </div>
        </div>
      }

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (teachers().length === 0) {
        <p class="empty">
          No teachers yet. A teacher need not be a member of the Samaaj - add their details, then
          assign them to a class from the class's own page.
        </p>
      } @else {
        <div class="table-wrap">
          <table>
            <caption class="sr-only">Teachers at this Pathshala</caption>
            <thead>
              <tr>
                <th>Name</th>
                <th>Contact</th>
                <th>Qualification</th>
                <th>Teaches</th>
                <th>Login</th>
                @if (canManage()) {
                  <th><span class="sr-only">Actions</span></th>
                }
              </tr>
            </thead>
            <tbody>
              @for (teacher of teachers(); track teacher.id) {
                <tr>
                  <td><b>{{ teacher.fullName }}</b></td>
                  <td>
                    @if (teacher.phone; as phone) {
                      <a [href]="'tel:' + phone">{{ phone }}</a>
                    }
                    @if (teacher.phone && teacher.email) { <br /> }
                    @if (teacher.email; as email) {
                      <a [href]="'mailto:' + email">{{ email }}</a>
                    }
                    @if (!teacher.phone && !teacher.email) {
                      <span class="muted">Not given</span>
                    }
                  </td>
                  <td>{{ teacher.qualification ?? '—' }}</td>
                  <td>{{ classNames(teacher) }}</td>
                  <td>
                    @if (teacher.hasLogin) {
                      <span class="pill ok">Can sign in</span>
                    } @else {
                      <span class="pill off">No login</span>
                    }
                  </td>
                  @if (canManage()) {
                    <td class="row-actions">
                      <button class="btn small alt" type="button" (click)="openEdit(teacher, $event)">
                        Edit
                      </button>
                      @if (canGrantLogins()) {
                        <button class="btn small alt" type="button" (click)="openLogin(teacher, $event)">
                          {{ teacher.hasLogin ? 'Login…' : 'Give a login…' }}
                        </button>
                      }
                      <button
                        class="btn small alt"
                        type="button"
                        [disabled]="busy()"
                        [attr.aria-expanded]="removing()?.id === teacher.id"
                        (click)="removing.set(teacher)"
                      >
                        Remove…
                      </button>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>

        @if (canManage() && !canGrantLogins()) {
          <p class="small">
            A teacher who should sign in - to mark registers or message parents - needs a login,
            which only the Samaaj Admin can give. Their details are enough to assign them a class.
          </p>
        }
      }
    </div>

    <!-- Add or edit ------------------------------------------------------- -->
    @if (editing(); as target) {
      <div class="modal-backdrop" (click)="closeEdit()">
        <div
          class="card modal-card modal-dialog"
          role="dialog"
          aria-modal="true"
          aria-labelledby="teacher-dialog-title"
          (click)="$event.stopPropagation()"
          (keydown)="onEditKeydown($event)"
        >
          <h2 id="teacher-dialog-title">
            {{ target === 'new' ? 'Add a teacher' : 'Edit ' + target.fullName }}
          </h2>

          @if (editError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <form (ngSubmit)="saveTeacher()">
            <label for="teacher-name">Full name</label>
            <input id="teacher-name" class="input" name="fullName" [(ngModel)]="fullName"
              maxlength="200" #firstField />

            <label for="teacher-phone">Phone</label>
            <input id="teacher-phone" class="input" name="phone" type="tel" [(ngModel)]="phone"
              maxlength="20" placeholder="+91 98290 12345"
              [attr.aria-invalid]="phoneInvalid() ? 'true' : null" />
            @if (phoneInvalid()) {
              <p class="small">Digits, spaces, brackets and dashes, 7 to 20 of them.</p>
            }

            <label for="teacher-email">Email</label>
            <input id="teacher-email" class="input" name="email" type="email" [(ngModel)]="email"
              maxlength="200" />

            <label for="teacher-qualification">Qualification</label>
            <input id="teacher-qualification" class="input" name="qualification"
              [(ngModel)]="qualification" maxlength="200" placeholder="Shastri, B.Ed." />

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canSave()">Save</button>
              <button class="btn alt" type="button" (click)="closeEdit()">Cancel</button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- Login ------------------------------------------------------------- -->
    @if (loginFor(); as teacher) {
      <div class="modal-backdrop" (click)="closeLogin()">
        <div
          class="card modal-card modal-dialog"
          role="dialog"
          aria-modal="true"
          aria-labelledby="login-dialog-title"
          (click)="$event.stopPropagation()"
          (keydown)="onLoginKeydown($event)"
        >
          <h2 id="login-dialog-title">A login for {{ teacher.fullName }}</h2>

          @if (loginError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          @if (activationCode(); as code) {
            <div class="notice ok" role="status">
              <p>
                {{ teacher.fullName }} has an account. Give them this one-time activation code - it
                is shown once and cannot be looked up again:
              </p>
              <p class="code">{{ code }}</p>
            </div>
            <div class="actions">
              <button class="btn" type="button" #firstField (click)="closeLogin()">Done</button>
            </div>
          } @else if (teacher.hasLogin) {
            <p>
              {{ teacher.fullName }} signs in with their own account, and can mark registers and
              message parents for the classes they teach.
            </p>
            <p class="small">
              Unlinking stops this Pathshala recognising them. Their account keeps the
              Pathshala Teacher role - they may teach elsewhere - so take it away on Admin Users
              if they should not.
            </p>
            <div class="actions">
              <button class="btn alt" type="button" [disabled]="busy()" #firstField (click)="unlink(teacher)">
                Unlink their login
              </button>
              <button class="btn alt" type="button" (click)="closeLogin()">Cancel</button>
            </div>
          } @else {
            <div class="tabs" role="tablist" aria-label="Whose login">
              <button type="button" role="tab" [class.on]="loginMode() === 'member'"
                [attr.aria-selected]="loginMode() === 'member'" #firstField
                (click)="loginMode.set('member')">
                They are a member
              </button>
              <button type="button" role="tab" [class.on]="loginMode() === 'new'"
                [attr.aria-selected]="loginMode() === 'new'" (click)="loginMode.set('new')">
                Create a login
              </button>
            </div>

            @if (loginMode() === 'member') {
              <form (ngSubmit)="searchMembers()">
                <label for="login-search">Find the member</label>
                <input id="login-search" class="input" name="term" [(ngModel)]="memberTerm"
                  placeholder="Name, mobile or email" />
                <div class="actions">
                  <button class="btn alt" type="submit" [disabled]="busy() || !memberTerm.trim()">
                    Search
                  </button>
                </div>
              </form>

              @if (memberResults(); as found) {
                @if (found.length === 0) {
                  <p class="empty">Nobody matches. Try their mobile number.</p>
                } @else {
                  <ul class="plain">
                    @for (member of found; track member.id) {
                      <li class="row-actions">
                        <span>{{ member.fullName }} · {{ member.mobile ?? member.email ?? '' }}</span>
                        <button class="btn small" type="button" [disabled]="busy()"
                          (click)="linkMember(teacher, member)">
                          Use this login
                        </button>
                      </li>
                    }
                  </ul>
                }
              }
              <p class="small">
                They are given the Pathshala Teacher role and sign in with the account they already
                have.
              </p>
            } @else {
              <form (ngSubmit)="createLogin(teacher)">
                <label for="login-identifier">Mobile number or email to sign in with</label>
                <input id="login-identifier" class="input" name="identifier" [(ngModel)]="identifier"
                  maxlength="200" />
                <p class="small">
                  For a teacher who is not a member of the Samaaj: an account holding only the
                  Pathshala Teacher role. It signs in to this admin panel, sees only the Pathshalas
                  they teach, and cannot use the member portal. They set their password with the
                  activation code shown next.
                </p>
                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !identifier.trim()">
                    Create the login
                  </button>
                  <button class="btn alt" type="button" (click)="closeLogin()">Cancel</button>
                </div>
              </form>
            }
          }
        </div>
      </div>
    }
  `,
  styles: `
    ul.plain {
      list-style: none;
      padding: 0;
      margin: 0;
    }

    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      align-items: center;
    }
  `,
})
export class PathshalaTeachersComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly pathshalaId = input.required<string>();
  readonly classes = input<readonly PathshalaClass[]>([]);
  readonly canManage = input(false);

  /** Removing a teacher takes them off classes, so the parent re-reads those. */
  readonly changed = output<void>();

  readonly teachers = signal<readonly PathshalaTeacher[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly removing = signal<PathshalaTeacher | null>(null);

  /** Granting a role is `Roles.Manage`, which an Area Admin does not hold. */
  readonly canGrantLogins = computed(() =>
    this.auth.roles().some((role) => role === 'SamaajAdmin' || role === 'SuperAdmin'),
  );

  // ---- Add or edit ---------------------------------------------------------

  readonly editing = signal<PathshalaTeacher | 'new' | null>(null);
  readonly editError = signal<string | null>(null);

  fullName = '';
  phone = '';
  email = '';
  qualification = '';

  // ---- Login ---------------------------------------------------------------

  readonly loginFor = signal<PathshalaTeacher | null>(null);
  readonly loginMode = signal<'member' | 'new'>('member');
  readonly loginError = signal<string | null>(null);
  readonly memberResults = signal<readonly AdminMember[] | null>(null);
  readonly activationCode = signal<string | null>(null);

  memberTerm = '';
  identifier = '';

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLElement>;
  private trigger: HTMLElement | null = null;

  ngOnInit(): void {
    this.load();
  }

  classNames(teacher: PathshalaTeacher): string {
    const names = this.classes()
      .filter((c) => teacher.classIds.includes(c.id))
      .map((c) => c.name);

    return names.length === 0 ? 'No class yet' : names.join(', ');
  }

  phoneInvalid(): boolean {
    return this.phone.trim().length > 0 && !PHONE.test(this.phone.trim());
  }

  canSave(): boolean {
    return this.fullName.trim().length > 0 && !this.phoneInvalid();
  }

  openAdd(event: MouseEvent): void {
    this.fullName = '';
    this.phone = '';
    this.email = '';
    this.qualification = '';
    this.openDialog(this.editing, 'new', event);
  }

  openEdit(teacher: PathshalaTeacher, event: MouseEvent): void {
    this.fullName = teacher.fullName;
    this.phone = teacher.phone ?? '';
    this.email = teacher.email ?? '';
    this.qualification = teacher.qualification ?? '';
    this.openDialog(this.editing, teacher, event);
  }

  closeEdit(): void {
    this.closeDialog(this.editing);
  }

  onEditKeydown(event: KeyboardEvent): void {
    modalKeydown(event, () => this.closeEdit());
  }

  saveTeacher(): void {
    const target = this.editing();

    if (target === null || !this.canSave() || this.busy()) {
      return;
    }

    const details: TeacherInput = {
      fullName: this.fullName.trim(),
      phone: blankToNull(this.phone),
      email: blankToNull(this.email),
      qualification: blankToNull(this.qualification),
      // Editing the details never changes who signs in as them.
      userId: target === 'new' ? null : target.userId,
    };

    const work =
      target === 'new'
        ? this.api.addTeacher(this.pathshalaId(), details)
        : this.api.updateTeacher(this.pathshalaId(), target.id, details);

    this.busy.set(true);
    this.editError.set(null);

    work.subscribe({
      next: () => {
        this.busy.set(false);
        this.closeEdit();
        this.done.set(target === 'new' ? `${details.fullName} added.` : `${details.fullName} saved.`);
        this.load();
      },
      error: (failure: unknown) => {
        // Kept in the dialog, so a refusal does not cost what was typed.
        this.editError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  remove(teacher: PathshalaTeacher): void {
    this.removing.set(null);
    this.act(
      this.api.removeTeacher(this.pathshalaId(), teacher.id),
      `${teacher.fullName} is no longer a teacher here.`,
      true,
    );
  }

  // ---- Login ---------------------------------------------------------------

  openLogin(teacher: PathshalaTeacher, event: MouseEvent): void {
    this.loginMode.set('member');
    this.loginError.set(null);
    this.memberResults.set(null);
    this.activationCode.set(null);
    this.memberTerm = teacher.phone ?? teacher.fullName;
    this.identifier = (teacher.phone ?? teacher.email ?? '').replace(/[\s()-]/g, '');
    this.openDialog(this.loginFor, teacher, event);
  }

  closeLogin(): void {
    // The code leaves the DOM with the dialog: it cannot be looked up again,
    // and leaving it on screen is how one is handed to the wrong person.
    this.activationCode.set(null);
    this.closeDialog(this.loginFor);
  }

  onLoginKeydown(event: KeyboardEvent): void {
    modalKeydown(event, () => this.closeLogin());
  }

  searchMembers(): void {
    const term = this.memberTerm.trim();

    if (term.length === 0) {
      return;
    }

    this.api.searchMembers(term, null, 0, 10).subscribe({
      next: (page) => this.memberResults.set(page.items),
      error: (failure: unknown) => this.loginError.set(describeError(failure)),
    });
  }

  /** A member's id is their account's id, so the role and the link name the same one. */
  linkMember(teacher: PathshalaTeacher, member: AdminMember): void {
    this.linkLogin(
      teacher,
      this.api
        .setRole(member.id, 'PathshalaTeacher', true)
        .pipe(switchMap(() => this.api.updateTeacher(this.pathshalaId(), teacher.id, withLogin(teacher, member.id)))),
      `${teacher.fullName} signs in as ${member.fullName}.`,
    );
  }

  createLogin(teacher: PathshalaTeacher): void {
    const mobileOrEmail = this.identifier.trim();

    if (mobileOrEmail.length === 0) {
      return;
    }

    this.linkLogin(
      teacher,
      this.api
        .inviteAdmin({ fullName: teacher.fullName, mobileOrEmail, roles: ['PathshalaTeacher'] })
        .pipe(
          // Kept before linking: the account exists either way, and a code that
          // was never shown is an account nobody can activate.
          tap((invited) => this.activationCode.set(invited.activationCode)),
          switchMap((invited) =>
            this.api.updateTeacher(this.pathshalaId(), teacher.id, withLogin(teacher, invited.userId)),
          ),
        ),
      null,
    );
  }

  unlink(teacher: PathshalaTeacher): void {
    this.linkLogin(
      teacher,
      this.api.updateTeacher(this.pathshalaId(), teacher.id, withLogin(teacher, null)),
      `${teacher.fullName}'s login is unlinked.`,
    );
  }

  /** `message` null keeps the dialog open, for the activation code. */
  private linkLogin(teacher: PathshalaTeacher, work: Observable<unknown>, message: string | null): void {
    this.busy.set(true);
    this.loginError.set(null);

    work.subscribe({
      next: () => {
        this.busy.set(false);

        if (message !== null) {
          this.closeLogin();
          this.done.set(message);
        } else {
          this.done.set(`${teacher.fullName} can sign in once they activate their account.`);
          setTimeout(() => this.firstFieldRef?.nativeElement.focus());
        }

        this.load();
      },
      error: (failure: unknown) => {
        this.loginError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Plumbing ------------------------------------------------------------

  private openDialog<T>(dialog: { set(value: T): void }, value: T, event: MouseEvent): void {
    this.trigger = event.currentTarget as HTMLElement;
    this.editError.set(null);
    this.done.set(null);
    dialog.set(value);
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  private closeDialog<T>(dialog: { (): T | null; set(value: T | null): void }): void {
    if (dialog() === null) {
      return;
    }

    dialog.set(null);

    const trigger = this.trigger;
    this.trigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  private load(): void {
    this.api.pathshalaTeachers(this.pathshalaId()).subscribe({
      next: (found) => {
        this.teachers.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  private act(work: Observable<unknown>, message: string, notify = false): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        this.load();

        if (notify) {
          this.changed.emit();
        }
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}

function withLogin(teacher: PathshalaTeacher, userId: string | null): TeacherInput {
  return {
    fullName: teacher.fullName,
    phone: teacher.phone,
    email: teacher.email,
    qualification: teacher.qualification,
    userId,
  };
}
