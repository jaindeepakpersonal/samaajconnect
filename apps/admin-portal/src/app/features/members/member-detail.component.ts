import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminMember } from '../../core/admin.models';

/**
 * One member, and the correction form.
 *
 * **The endpoint this calls was written for this screen, because the one that
 * already existed could not be used.** `PATCH /v1/members/{id}` accepted a
 * Samaaj administrator and replaced the profile whole, so it required the
 * member's privacy levels and whether they are listed — and no read available to
 * an administrator returns either. Correcting a misspelt name therefore meant
 * sending settings the caller had no way to know, and an unreadable level parses
 * as Private, so the likeliest accident was quietly hiding every field the
 * member had chosen to share. `PATCH /v1/members/{id}/details` carries no
 * privacy field at all.
 *
 * That is why this form has no privacy controls and no "listed in the
 * directory" tick. They are not omitted for space: an administrator cannot set
 * them, and a control that cannot be saved is worse than an absent one. The
 * note under the form says so rather than leaving it to be discovered.
 *
 * **Every field is shown filled in, including ones the member marked Private.**
 * `IsVisibleTo` lets a Samaaj admin past every level — correcting details is
 * the job — so unlike the member portal's directory, a null here means "not
 * set" and never "not shared". The form must send the values back unchanged, so
 * showing them is not a courtesy, it is what stops a correction from wiping a
 * field the administrator was never shown.
 */
@Component({
  selector: 'app-member-detail',
  imports: [FormsModule, RouterLink],
  template: `
    <a class="back" routerLink="/members">‹ Back to Members</a>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (member(); as person) {
      <h1 class="title">{{ person.fullName }}</h1>
      <p class="sub">
        Correcting somebody's details on their behalf. What they share with the Samaaj stays
        their decision.
      </p>

      @if (saved()) {
        <p class="notice ok" role="status">
          Saved. The change is recorded against your account in the audit log, with the names
          of the fields you changed.
        </p>
      }

      <div class="card">
        <h2>Details</h2>

        <form (ngSubmit)="save()">
          <div class="field">
            <label for="fullName">Full name</label>
            <input
              class="input"
              id="fullName"
              name="fullName"
              required
              maxlength="200"
              [(ngModel)]="form.fullName"
              [attr.aria-invalid]="nameMissing() ? 'true' : null"
            />
            @if (nameMissing()) {
              <p class="small error-text" role="alert">A member has to have a name.</p>
            }
          </div>

          <div class="grid2">
            <div class="field">
              <label for="dateOfBirth">Date of birth</label>
              <input
                class="input"
                id="dateOfBirth"
                name="dateOfBirth"
                type="date"
                [(ngModel)]="form.dateOfBirth"
              />
            </div>

            <div class="field">
              <label for="gender">Gender</label>
              <select class="input" id="gender" name="gender" [(ngModel)]="form.gender">
                @for (option of genders; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="mobile">Mobile</label>
              <input
                class="input"
                id="mobile"
                name="mobile"
                maxlength="20"
                [(ngModel)]="form.mobile"
              />
            </div>

            <div class="field">
              <label for="email">Email</label>
              <input
                class="input"
                id="email"
                name="email"
                maxlength="320"
                [(ngModel)]="form.email"
              />
            </div>

            <div class="field">
              <label for="locality">Locality</label>
              <input
                class="input"
                id="locality"
                name="locality"
                maxlength="120"
                [(ngModel)]="form.locality"
              />
            </div>

            <div class="field">
              <label for="profession">Profession</label>
              <input
                class="input"
                id="profession"
                name="profession"
                maxlength="120"
                [(ngModel)]="form.profession"
              />
            </div>
          </div>

          <div class="field">
            <label for="address">Address</label>
            <input
              class="input"
              id="address"
              name="address"
              maxlength="500"
              [(ngModel)]="form.address"
            />
          </div>

          <p class="small">
            You are shown every field, including any this member marked private, because
            correcting them is what this screen is for. What they share, and whether they
            appear in the member directory, are theirs to change and cannot be changed here.
          </p>

          <div class="actions">
            <button class="btn" type="submit" [disabled]="saving()">
              {{ saving() ? 'Saving…' : 'Save corrections' }}
            </button>
            <a class="btn alt" routerLink="/members">Cancel</a>
          </div>
        </form>
      </div>
    }
  `,
  styles: `
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      margin-bottom: 12px;
    }

    .error-text {
      color: var(--danger-text, #b00020);
    }
  `,
})
export class MemberDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly member = signal<AdminMember | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly error = signal<string | null>(null);

  /** The service's closed list. A value outside it is a validation problem. */
  readonly genders = ['Unspecified', 'Male', 'Female', 'Other'];

  form = {
    fullName: '',
    dateOfBirth: '',
    gender: 'Unspecified',
    mobile: '',
    email: '',
    address: '',
    locality: '',
    profession: '',
  };

  nameMissing(): boolean {
    return this.form.fullName.trim() === '';
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      return;
    }

    this.api.member(id).subscribe({
      next: (person) => {
        this.member.set(person);
        this.fill(person);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  private fill(person: AdminMember): void {
    // Empty strings rather than nulls, because an `undefined` bound to an input
    // renders as the string "undefined" and an `undefined` bound to a `<select>`
    // matches no option at all - the control renders blank rather than showing
    // its first choice, which is the member portal's Pathshala picker bug.
    this.form = {
      fullName: person.fullName,
      dateOfBirth: person.dateOfBirth ?? '',
      gender: person.gender,
      mobile: person.mobile ?? '',
      email: person.email ?? '',
      address: person.address ?? '',
      locality: person.locality ?? '',
      profession: person.profession ?? '',
    };
  }

  save(): void {
    const person = this.member();

    if (person === null || this.nameMissing()) {
      return;
    }

    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);

    this.api
      .correctMember(person.id, {
        fullName: this.form.fullName.trim(),
        // Blank means "cleared", which is a real correction: a wrong number an
        // administrator is asked to remove has to be removable. The service
        // normalises whitespace to null, so an empty box and a box of spaces
        // are the same thing.
        dateOfBirth: this.blank(this.form.dateOfBirth),
        gender: this.form.gender,
        mobile: this.blank(this.form.mobile),
        email: this.blank(this.form.email),
        address: this.blank(this.form.address),
        locality: this.blank(this.form.locality),
        profession: this.blank(this.form.profession),
      })
      .subscribe({
        next: (updated) => {
          this.member.set(updated);
          this.fill(updated);
          this.saving.set(false);
          this.saved.set(true);
        },
        error: (failure: unknown) => {
          this.error.set(describeError(failure));
          this.saving.set(false);
        },
      });
  }

  private blank(value: string): string | null {
    return value.trim() === '' ? null : value.trim();
  }
}
